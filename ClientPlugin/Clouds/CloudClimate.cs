using System;
using System.Runtime.InteropServices;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Clouds;

/// <summary>
/// Live 64² weather on the equirectangular longitude/latitude grid.
/// Ice, slope, and daylight stay on the ground. Four highs and eight lows
/// turn the wind. The render thread uploads that RGBA.
/// </summary>
public static class CloudClimate
{
    public const int Size = 64;
    const int CellCount = Size * Size;
    const float StepSeconds = 0.25f;
    // Jet core at wind 1: about four texel-widths per real minute (a few
    // sim-hours). Slow enough that a step moves a fraction of a texel.
    const float JetTexelsPerRealMinute = 4f;
    const float DeclinationCap = 35f * (float)Math.PI / 180f;

    static readonly object SimLock = new();
    static readonly object PublishLock = new();
    static int planet = int.MinValue;
    static float[] height;
    static float hill = 1f;
    static Vector3 axis = Vector3.Up;
    static float spacing = 1f;
    static float hillColumn = 0.5f;
    static float oceanDatum;
    static float oceanFraction = 0.5f;
    static bool oceanFromLowlands;
    static bool materialsFound;
    static float[] waterIce;
    static float[] coldIce;
    static float waterFraction;
    static float coldFraction;
    static float fallbackDec;
    static float[] humidity;
    static float[] cloud;
    static float[] humiditySrc;
    static float[] cloudSrc;
    static byte[] rgba;
    static byte[] surface;
    static byte[] published;
    static byte[] publishedSurface;
    static bool dirty;
    static float accum;
    static int steps;
    static bool loggedSunFail;
    const int StormCount = 8;
    const int HighCount = 4;
    const float HighStrength = 0.9f;
    static Storm[] storms;
    static int liveStorms;
    static int liveCyclones;
    static int liveSuper;
    static int liveTornado;
    static float[] cycloneMask;
    static StormObject[] objects;
    static float seaRadius;
    static Matrix planetBasis = Matrix.Identity;
    static bool planetBasisSet;
    static HighCenter[] highs;
    static int liveHighs;
    static float[] pressure;

    public const int VolumeBytes = CellCount * 4 * 2;

    public static int PlanetSeed => planet;
    public static int Steps => steps;
    public static bool Running => humidity != null && planet != int.MinValue;

    /// <summary>
    /// Status. With surface voxels, ice that has no snow nearby is water and
    /// snow (or ice beside snow) is cold. Without those materials the elevation
    /// datum still supplies moisture, including the lowland fill.
    /// </summary>
    public static string MoistureLine =>
        (materialsFound
            ? $"water ice {waterFraction * 100f:0}% · cold {coldFraction * 100f:0}%"
            : oceanFromLowlands
                ? $"lowlands {oceanFraction * 100f:0}% moist"
                : $"ocean {oceanFraction * 100f:0}%")
        + $" · storms {liveStorms} · highs {liveHighs} · cyclones {liveCyclones} · super {liveSuper} · tornado {liveTornado}";

    public static void Reset()
    {
        lock (SimLock)
        lock (PublishLock)
        {
            planet = int.MinValue;
            height = null;
            waterIce = null;
            coldIce = null;
            materialsFound = false;
            waterFraction = 0f;
            coldFraction = 0f;
            humidity = null;
            cloud = null;
            humiditySrc = null;
            cloudSrc = null;
            rgba = null;
            surface = null;
            published = null;
            publishedSurface = null;
            dirty = false;
            accum = 0f;
            steps = 0;
            storms = null;
            liveStorms = 0;
            liveCyclones = 0;
            liveSuper = 0;
            liveTornado = 0;
            cycloneMask = null;
            objects = null;
            seaRadius = 0f;
            planetBasis = Matrix.Identity;
            planetBasisSet = false;
            highs = null;
            liveHighs = 0;
            pressure = null;
        }
    }

    /// <summary>Game thread. Elevation is reused. A new planet reseeds the field.</summary>
    public static void Bind(int planetSeed, float declinationRad, float spacingValue, float[] heightAboveSea, float hillAmplitude, Vector3 spinAxis, float hillColumnFraction, float[] waterIceFraction = null, float[] coldIceFraction = null, float seaMetres = 0f)
    {
        lock (SimLock)
        BindUnlocked(planetSeed, declinationRad, spacingValue, heightAboveSea, hillAmplitude, spinAxis, hillColumnFraction, waterIceFraction, coldIceFraction, seaMetres);
    }

    static void BindUnlocked(int planetSeed, float declinationRad, float spacingValue, float[] heightAboveSea, float hillAmplitude, Vector3 spinAxis, float hillColumnFraction, float[] waterIceFraction, float[] coldIceFraction, float seaMetres)
    {
        bool fresh = planet != planetSeed || humidity == null;
        planet = planetSeed;
        seaRadius = seaMetres > 1f ? seaMetres : 60000f;
        spacing = MathHelper.Clamp(spacingValue, 0.35f, 3f);
        hill = hillAmplitude > 1f ? hillAmplitude : 1f;
        hillColumn = MathHelper.Clamp(hillColumnFraction, 0.15f, 0.85f);
        height = heightAboveSea != null && heightAboveSea.Length == CellCount ? heightAboveSea : null;
        waterIce = waterIceFraction != null && waterIceFraction.Length == CellCount ? waterIceFraction : null;
        coldIce = coldIceFraction != null && coldIceFraction.Length == CellCount ? coldIceFraction : null;
        axis = spinAxis.LengthSquared() > 1e-6f ? Vector3.Normalize(spinAxis) : Vector3.Up;
        fallbackDec = MathHelper.Clamp(declinationRad, -DeclinationCap, DeclinationCap);
        MeasureOcean();
        if (!fresh)
            return;

        humidity = new float[CellCount];
        cloud = new float[CellCount];
        humiditySrc = new float[CellCount];
        cloudSrc = new float[CellCount];
        rgba = new byte[CellCount * 4];
        surface = new byte[CellCount];
        accum = 0f;
        steps = 0;
        storms = new Storm[StormCount];
        objects = new StormObject[StormCount];
        ReadSun(out _, out float dec);
        Seed(dec);
        Publish();
        MyLog.Default.Info($"{Plugin.Name}: climate sim seeded {Size}² planet {planetSeed} · {MoistureLine}");
    }

    /// <summary>Game thread. Steps about four times a second. Does not touch D3D or disk.</summary>
    public static bool Step(float realDt)
    {
        lock (SimLock)
            return StepUnlocked(realDt);
    }

    static bool StepUnlocked(float realDt)
    {
        if (humidity == null)
            return false;
        if (realDt <= 0f || realDt > 0.5f)
            realDt = 1f / 60f;

        accum += realDt;
        int guard = 0;
        while (accum >= StepSeconds && guard++ < 3)
        {
            accum -= StepSeconds;
            ReadSun(out Vector3 sun, out float dec);
            float wind = Math.Max(Config.Current.WindSpeed, 0f);
            Advance(StepSeconds, sun, dec, wind);
            steps++;
        }

        if (guard == 0)
            return false;
        Publish();
        return true;
    }

    public static byte[] CopyPixels()
    {
        lock (PublishLock)
            return published == null ? null : (byte[])published.Clone();
    }

    /// <summary>
    /// Slice 0 is the weather RGBA. Slice 1 R is the local surface as a fraction
    /// of the sea-to-air column, so the deck can sit above the hills.
    /// </summary>
    public static bool CopyVolume(byte[] dst, bool onlyIfDirty)
    {
        if (dst == null || dst.Length < VolumeBytes)
            return false;
        lock (PublishLock)
        {
            if (published == null)
                return false;
            if (onlyIfDirty && !dirty)
                return false;
            Buffer.BlockCopy(published, 0, dst, 0, CellCount * 4);
            int slice = CellCount * 4;
            if (publishedSurface != null && publishedSurface.Length == CellCount)
            {
                for (int i = 0; i < CellCount; i++)
                {
                    int o = slice + i * 4;
                    dst[o] = publishedSurface[i];
                    dst[o + 1] = 0;
                    dst[o + 2] = 0;
                    dst[o + 3] = 255;
                }
            }
            else
            {
                Array.Clear(dst, slice, slice);
            }

            if (onlyIfDirty)
                dirty = false;
            return true;
        }
    }

    public static void ClearDirty()
    {
        lock (PublishLock)
            dirty = false;
    }

    static void Publish()
    {
        WriteImage();
        lock (PublishLock)
        {
            if (published == null || published.Length != rgba.Length)
                published = new byte[rgba.Length];
            Buffer.BlockCopy(rgba, 0, published, 0, rgba.Length);
            if (surface != null)
            {
                if (publishedSurface == null || publishedSurface.Length != surface.Length)
                    publishedSurface = new byte[surface.Length];
                Buffer.BlockCopy(surface, 0, publishedSurface, 0, surface.Length);
            }
            dirty = true;
        }
    }

    static void Seed(float dec)
    {
        var sun = Vector3.Zero;
        PlaceHighs();
        if (storms == null || storms.Length != StormCount)
            storms = new Storm[StormCount];
        if (objects == null || objects.Length != StormCount)
            objects = new StormObject[StormCount];
        for (int i = 0; i < StormCount; i++)
            Reborn(ref storms[i], i, dec, sun);
        FillPressure();

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float floor = SourceFloor(Evaluate(x, y, dec, sun), x, y);
                int i = y * Size + x;
                humidity[i] = floor;
                cloud[i] = floor;
            }
        }

        ClearCycloneMask();
        for (int i = 0; i < StormCount; i++)
            StampStorm(storms[i], dec, sun);
        liveStorms = StormCount;
        liveCyclones = CountCyclones();
        CountStages();
    }

    static void Advance(float dt, Vector3 sun, float dec, float windSpeed)
    {
        float texelsPerSecond = (JetTexelsPerRealMinute / 60f) * windSpeed;
        if (highs == null || highs.Length != HighCount)
            PlaceHighs();
        if (storms == null || storms.Length != StormCount)
            Seed(dec);

        StepHighs(dt, texelsPerSecond, dec, sun);
        FillPressure();
        Array.Copy(humidity, humiditySrc, CellCount);
        Array.Copy(cloud, cloudSrc, CellCount);
        // Wake fades in a couple of minutes. The source floor is the
        // attractor at the destination cell, so topography does not slide.
        float wake = 1f - (float)Math.Exp(-0.008f * dt);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var cell = Evaluate(x, y, dec, sun);
                PressureWind(x, y, cell.Thermal, out float vx, out float vy);
                float slow = 1f - 0.65f * Saturate(cell.Slope);
                float shiftX = vx * slow * texelsPerSecond * dt;
                float shiftY = vy * slow * texelsPerSecond * dt;
                int i = y * Size + x;
                float floor = SourceFloor(cell, x, y);
                humidity[i] = Saturate(Lerp(Sample(humiditySrc, x - shiftX, y - shiftY), floor, wake));
                cloud[i] = Saturate(Lerp(Sample(cloudSrc, x - shiftX, y - shiftY), floor, wake));
            }
        }

        ClearCycloneMask();
        int alive = 0;
        for (int i = 0; i < StormCount; i++)
        {
            StepStorm(ref storms[i], i, dt, texelsPerSecond, dec, sun);
            StampStorm(storms[i], dec, sun);
            if (storms[i].Intensity >= 0.08f)
                alive++;
        }

        liveStorms = alive;
        liveCyclones = CountCyclones();
        CountStages();
    }

    static void WriteImage()
    {
        ReadSun(out Vector3 sun, out float dec);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var cell = Evaluate(x, y, dec, sun);
                int i = y * Size + x;
                float c = cloud[i];
                float jet = Math.Max(Gauss(cell.Thermal, 34f, 8f), Gauss(cell.Thermal, -34f, 8f));
                float live = Saturate(c * 1.35f);
                // Keep the deck base above the stratus band. Dropping it to 0.08
                // classified the ITCZ as terrain-hug stratus and ran it through the hills.
                float baseH = MathHelper.Clamp(0.58f - 0.28f * cell.Itcz - 0.12f * cell.Storm - 0.10f * cell.Elev + 0.12f * cell.Dry, 0.18f, 0.9f);
                float warm = 1f - cell.Cold;
                float thick = MathHelper.Clamp(0.22f + (0.50f * cell.Itcz + 0.30f * cell.Storm + 0.22f * cell.Lift) * warm - 0.16f * cell.Dry + 0.06f * cell.Cold, 0.08f, 1f);
                float cyc = cycloneMask != null ? cycloneMask[i] : 0f;
                if (cyc > 0f)
                {
                    baseH = Math.Max(0.18f, Lerp(baseH, 0.22f, cyc));
                    thick = Lerp(thick, 0.85f, cyc);
                }
                thick = Lerp(thick * 0.65f, thick, live);
                float cirrus = MathHelper.Clamp(0.08f + 0.55f * jet + 0.28f * cell.Polar + 0.18f * live, 0f, 1f);
                int p = i * 4;
                rgba[p] = ToByte(c);
                rgba[p + 1] = ToByte(baseH);
                rgba[p + 2] = ToByte(thick);
                rgba[p + 3] = ToByte(cirrus);
                if (surface != null)
                    surface[i] = ToByte(cell.Surface);
            }
        }
    }

    static void CellFrame(int x, int y, float dec, Vector3 sun, out float thermalDeg, out float elev, out float slope, out float day)
    {
        float u = (x + 0.5f) / Size;
        float v = (y + 0.5f) / Size;
        float lat = (v - 0.5f) * (float)Math.PI;
        float lon = (u - 0.5f) * (float)Math.PI * 2f;
        float cosLat = (float)Math.Cos(lat);
        var dir = new Vector3(cosLat * (float)Math.Sin(lon), (float)Math.Sin(lat), cosLat * (float)Math.Cos(lon));
        float geo = (float)Math.Asin(MathHelper.Clamp(Vector3.Dot(dir, axis), -1f, 1f));
        thermalDeg = geo * (180f / (float)Math.PI) - dec * (180f / (float)Math.PI);
        elev = 0f;
        slope = 0f;
        if (height != null)
        {
            float h = height[y * Size + x];
            elev = Saturate(h / hill);
            float zonal = Zonal(thermalDeg);
            int upwind = Math.Abs(zonal) < 0.08f ? 0 : (zonal > 0f ? -1 : 1);
            float from = height[y * Size + Mod(x + upwind, Size)];
            slope = MathHelper.Clamp((h - from) / hill, -1f, 1f);
        }

        day = sun.LengthSquared() > 1e-6f ? Saturate(Vector3.Dot(dir, sun) * 2f) : 0.65f;
    }

    static float Zonal(float thermalDeg)
    {
        float absT = Math.Abs(thermalDeg);
        float west = Gauss(absT, 48f, 16f);
        float east = Math.Max(Gauss(absT, 14f, 14f), Gauss(absT, 78f, 12f));
        return (west - east) / Math.Max(west + east, 0.2f);
    }

    static float Meridional(float thermalDeg)
    {
        float absT = Math.Abs(thermalDeg);
        float sign = thermalDeg >= 0f ? 1f : -1f;
        float trade = Gauss(absT, 14f, 14f);
        float west = Gauss(absT, 48f, 16f);
        return (-trade + 0.35f * west) * sign * 0.35f;
    }

    static float DryBelt(float thermalDeg) =>
        Math.Max(Gauss(thermalDeg, 28f, 9f * Math.Max(spacing, 0.75f)), Gauss(thermalDeg, -28f, 9f * Math.Max(spacing, 0.75f)));

    static float StormBelt(float thermalDeg)
    {
        float width = 11f * spacing;
        return Math.Max(Gauss(thermalDeg, 50f, width), Gauss(thermalDeg, -50f, width));
    }

    struct CellForce
    {
        public float Thermal;
        public float Elev;
        public float Slope;
        public float Day;
        public float Itcz;
        public float Dry;
        public float Storm;
        public float Polar;
        public float Ocean;
        public float Cold;
        public float Lift;
        public float Lee;
        public float Surface;
    }

    static CellForce Evaluate(int x, int y, float dec, Vector3 sun)
    {
        CellFrame(x, y, dec, sun, out float thermal, out float elev, out float slope, out float day);
        float h = height != null ? height[y * Size + x] : 0f;
        float water = FractionAt(waterIce, x, y);
        float cold = FractionAt(coldIce, x, y);
        return new CellForce
        {
            Thermal = thermal,
            Elev = elev,
            Slope = slope,
            Day = day,
            Itcz = Gauss(thermal, 0f, 12f),
            Dry = DryBelt(thermal),
            Storm = StormBelt(thermal),
            Polar = Smooth01((Math.Abs(thermal) - 68f) / 14f),
            Ocean = materialsFound ? water : OceanAmount(h),
            Cold = cold,
            Lift = Math.Max(slope, 0f),
            Lee = Math.Max(-slope, 0f),
            Surface = SurfaceColumn(h),
        };
    }

    /// <summary>Stationary. Water and windward slopes, plus a weak cold-ice term. Highs clear it. Does not advect.</summary>
    static float SourceFloor(CellForce cell, int x, int y)
    {
        float ignite = Lerp(0.65f, 1f, cell.Day);
        float marine = cell.Ocean * 0.45f + cell.Lift * 0.28f;
        float cold = cell.Cold * 0.40f;
        // A dry planet (no water ice) was staying under the march's empty cut.
        float baseFloor = Saturate((0.50f + marine + cold) * ignite - cell.Lee * 0.06f);
        return Saturate(baseFloor * (1f - 0.35f * HighCover(x, y)));
    }

    static float MarineFeed(CellForce cell) =>
        (cell.Ocean * 0.75f + cell.Lift * 0.55f) * (0.45f + 0.55f * Math.Max(cell.Itcz, cell.Storm));

    static float ColdFeed(CellForce cell) =>
        cell.Cold * 0.80f * (0.35f + 0.65f * Math.Max(cell.Storm, cell.Polar));

    struct Storm
    {
        public float X;
        public float Y;
        public float Radius;
        public float Intensity;
        public float Age;
        public int Kind;
        public float FeedTime;
        public float Organize;
    }

    struct HighCenter
    {
        public float X;
        public float Y;
        public float HomeY;
        public float Radius;
        public float Strength;
    }

    static float StormRadius(int kind)
    {
        float scale = MathHelper.Clamp(spacing, 0.35f, 3f);
        float native = kind == 0 ? 3.1f : 2.2f;
        return MathHelper.Clamp(native * scale, 1.5f, 8f);
    }

    static float HighRadius()
    {
        float scale = MathHelper.Clamp(spacing, 0.75f, 1.5f);
        return MathHelper.Clamp(8f * scale, 8f, 12f);
    }

    static float LatitudeToY(float degrees)
    {
        float lat = degrees * (float)Math.PI / 180f;
        float v = 0.5f + lat / (float)Math.PI;
        return MathHelper.Clamp(v * Size - 0.5f, 1f, Size - 2f);
    }

    static void PlaceHighs()
    {
        if (highs == null || highs.Length != HighCount)
            highs = new HighCenter[HighCount];
        float radius = HighRadius();
        float north = LatitudeToY(28f);
        float south = LatitudeToY(-28f);
        PlaceHigh(0, 8.5f, north, radius);
        PlaceHigh(1, 40.5f, north, radius);
        PlaceHigh(2, 24.5f, south, radius);
        PlaceHigh(3, 56.5f, south, radius);
        liveHighs = HighCount;
    }

    static void PlaceHigh(int index, float x, float y, float radius)
    {
        highs[index].X = x;
        highs[index].Y = y;
        highs[index].HomeY = y;
        highs[index].Radius = radius;
        highs[index].Strength = HighStrength;
    }

    static void StepHighs(float dt, float texelsPerSecond, float dec, Vector3 sun)
    {
        if (highs == null || texelsPerSecond <= 0f)
            return;
        float pull = 1f - (float)Math.Exp(-0.015f * dt);
        for (int i = 0; i < HighCount; i++)
        {
            int x = Mod((int)Math.Floor(highs[i].X), Size);
            int y = (int)Math.Floor(highs[i].Y);
            if (y < 0)
                y = 0;
            else if (y >= Size)
                y = Size - 1;
            var cell = Evaluate(x, y, dec, sun);
            float shift = 0.25f * Zonal(cell.Thermal) * texelsPerSecond * dt;
            highs[i].X = Wrap(highs[i].X + shift);
            highs[i].Y = MathHelper.Clamp(Lerp(highs[i].Y, highs[i].HomeY, pull), 1f, Size - 2f);
            highs[i].Radius = HighRadius();
        }
    }

    static void FillPressure()
    {
        if (pressure == null || pressure.Length != CellCount)
            pressure = new float[CellCount];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float p = 0f;
                float px = x + 0.5f;
                float py = y + 0.5f;
                if (highs != null)
                {
                    for (int i = 0; i < HighCount; i++)
                        p += highs[i].Strength * GaussBump(px, py, highs[i].X, highs[i].Y, highs[i].Radius);
                }

                if (storms != null)
                {
                    for (int i = 0; i < storms.Length; i++)
                    {
                        if (storms[i].Intensity < 0.02f)
                            continue;
                        p -= storms[i].Intensity * GaussBump(px, py, storms[i].X, storms[i].Y, Math.Max(storms[i].Radius, 1.5f));
                    }
                }

                pressure[y * Size + x] = p;
            }
        }
    }

    static float GaussBump(float x, float y, float cx, float cy, float radius)
    {
        float dx = WrapDelta(x - cx);
        float dy = y - cy;
        float r = Math.Max(radius, 0.5f);
        float u = (dx * dx + dy * dy) / (r * r);
        return (float)Math.Exp(-u);
    }

    static float PressureAt(int x, int y)
    {
        if (pressure == null)
            return 0f;
        x = Mod(x, Size);
        if (y < 0)
            y = 0;
        else if (y >= Size)
            y = Size - 1;
        return pressure[y * Size + x];
    }

    static float HighCover(int x, int y)
    {
        if (highs == null)
            return 0f;
        float cover = 0f;
        float px = x + 0.5f;
        float py = y + 0.5f;
        for (int i = 0; i < HighCount; i++)
            cover += GaussBump(px, py, highs[i].X, highs[i].Y, highs[i].Radius);
        return Saturate(cover);
    }

    static void PressureWind(int x, int y, float thermalDeg, out float vx, out float vy)
    {
        float dpx = PressureAt(x + 1, y) - PressureAt(x - 1, y);
        float dpy = PressureAt(x, y + 1) - PressureAt(x, y - 1);
        float turn = (thermalDeg >= 0f ? 1f : -1f) * Saturate(Math.Abs(thermalDeg) / 20f);
        const float gradScale = 1.6f;
        float circX = -turn * dpy * gradScale;
        float circY = turn * dpx * gradScale;
        float circ = (float)Math.Sqrt(circX * circX + circY * circY);
        if (circ > 1.2f)
        {
            float s = 1.2f / circ;
            circX *= s;
            circY *= s;
        }

        vx = circX + 0.35f * Zonal(thermalDeg);
        vy = circY + 0.35f * Meridional(thermalDeg);
    }

    static void StepStorm(ref Storm storm, int index, float dt, float texelsPerSecond, float dec, Vector3 sun)
    {
        int x = Mod((int)Math.Floor(storm.X), Size);
        int y = (int)Math.Floor(storm.Y);
        if (y < 0)
            y = 0;
        else if (y >= Size)
            y = Size - 1;
        var cell = Evaluate(x, y, dec, sun);
        PressureWind(x, y, cell.Thermal, out float vx, out float vy);
        float slow = 1f - 0.65f * Saturate(cell.Slope);
        float step = slow * texelsPerSecond * dt;
        storm.X = Wrap(storm.X + vx * step);
        storm.Y = MathHelper.Clamp(storm.Y + vy * step, 0f, Size - 1.001f);
        storm.Age += dt;

        float feed = (storm.Kind == 0 ? MarineFeed(cell) : ColdFeed(cell)) * Lerp(0.55f, 1f, cell.Day);
        feed = Math.Max(feed, 0.42f);
        float drain = cell.Lee * 0.07f;
        if (cell.Day < 0.12f)
            drain += 0.018f;
        storm.Intensity = Saturate(storm.Intensity + (feed * 0.055f - drain) * dt);
        StepCyclone(ref storm, cell, x, y, dt);
        float native = StormRadius(storm.Kind);
        float tight = Math.Max(2.4f, native * 0.72f);
        storm.Radius = Lerp(native, tight, storm.Organize);
        StepObject(index, storm, cell, feed, dt);
        if (storm.Intensity < 0.08f)
            Reborn(ref storm, index, dec, sun);
    }

    static void StepCyclone(ref Storm storm, CellForce cell, int x, int y, float dt)
    {
        if (storm.Kind != 0)
        {
            storm.Organize = 0f;
            storm.FeedTime = 0f;
            return;
        }

        float absT = Math.Abs(cell.Thermal);
        float belt = StormBelt(cell.Thermal);
        float high = HighCover(x, y);
        bool clock = cell.Ocean >= 0.45f
            && cell.Cold <= 0.20f
            && high <= 0.35f
            && absT >= 8f && absT <= 24f
            && belt <= 0.25f;
        if (clock)
            storm.FeedTime += dt;
        else
            storm.FeedTime = Math.Max(0f, storm.FeedTime - 0.5f * dt);

        bool leaving = cell.Ocean < 0.28f
            || cell.Cold > 0.40f
            || high > 0.55f
            || absT > 32f
            || belt > 0.45f;
        if (leaving)
        {
            storm.Organize = Math.Max(0f, storm.Organize - 0.035f * dt);
            storm.Intensity = Saturate(storm.Intensity - 0.015f * dt);
            return;
        }

        if (storm.FeedTime >= 45f && storm.Intensity >= 0.70f)
        {
            storm.Organize = Math.Min(1f, storm.Organize + 0.05f * dt);
            storm.Intensity = Saturate(storm.Intensity + 0.04f * dt);
        }
    }

    static void Reborn(ref Storm storm, int index, float dec, Vector3 sun)
    {
        float best = -1f;
        int bx = index * 7 % Size;
        int by = Size / 2;
        int kind = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var cell = Evaluate(x, y, dec, sun);
                float marine = cell.Ocean * cell.Day * (0.6f + cell.Itcz + cell.Storm) + cell.Lift * cell.Day;
                float cold = cell.Cold * (cell.Storm + cell.Polar) * 0.5f * Math.Max(cell.Day, 0.35f);
                float score = Math.Max(marine, cold) + Hash01(x, y, planet + index * 17) * 0.08f;
                score -= StormCrowd(x + 0.5f, y + 0.5f, index);
                if (score > best)
                {
                    best = score;
                    bx = x;
                    by = y;
                    kind = cold > marine ? 1 : 0;
                }
            }
        }

        storm.X = bx + 0.5f;
        storm.Y = by + 0.5f;
        storm.Kind = kind;
        storm.Radius = StormRadius(kind);
        storm.Intensity = 0.4f;
        storm.Age = 0f;
        storm.FeedTime = 0f;
        storm.Organize = 0f;
        ResetObject(index);
    }

    static float StormCrowd(float x, float y, int skip)
    {
        if (storms == null)
            return 0f;
        float near = 0f;
        for (int i = 0; i < storms.Length; i++)
        {
            if (i == skip || storms[i].Intensity < 0.08f)
                continue;
            float dx = WrapDelta(x - storms[i].X);
            float dy = y - storms[i].Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d < 8f)
                near = Math.Max(near, (1f - d / 8f) * 0.45f);
        }

        return near;
    }

    static void StampStorm(Storm storm, float dec, Vector3 sun)
    {
        if (storm.Intensity < 0.02f || cloud == null)
            return;
        int cx = Mod((int)Math.Floor(storm.X), Size);
        int cy = (int)Math.Floor(storm.Y);
        if (cy < 0)
            cy = 0;
        else if (cy >= Size)
            cy = Size - 1;
        var cell = Evaluate(cx, cy, dec, sun);
        PressureWind(cx, cy, cell.Thermal, out float vx, out float vy);
        float len = (float)Math.Sqrt(vx * vx + vy * vy);
        if (len < 0.15f)
        {
            vx = 1f;
            vy = 0f;
            len = 1f;
        }

        float tx = vx / len;
        float ty = vy / len;
        float px = -ty;
        float py = tx;
        float radius = Math.Max(storm.Radius * 1.35f, 1.6f);
        float amount = Math.Max(storm.Intensity, 0.62f) * (storm.Kind == 0 ? 1f : 0.9f);
        int reach = (int)Math.Ceiling(radius) + 1;
        for (int dy = -reach; dy <= reach; dy++)
        {
            int y = cy + dy;
            if (y < 0 || y >= Size)
                continue;
            for (int dx = -reach; dx <= reach; dx++)
            {
                float along = dx * tx + dy * ty;
                float across = dx * px + dy * py;
                float rad = (float)Math.Sqrt(along * along + across * across);
                if (rad > radius)
                    continue;
                float ang = (float)Math.Atan2(across, along);
                float spiral = ang - 2.2f * (rad / radius);
                float arm = (float)Math.Exp(-2.2 * (1.0 - Math.Cos(spiral)));
                float disk = 1f - rad / radius;
                float spiralShape = disk * Lerp(0.45f, 1f, Saturate(arm));
                float eye = radius * 0.34f * storm.Organize;
                float wall = Math.Max(radius * 0.22f, 0.4f);
                float ringIn = Smooth01((rad - eye) / wall);
                float ringOut = 1f - Smooth01((rad - radius * 0.92f) / Math.Max(radius * 0.12f, 0.3f));
                float ring = ringIn * ringOut;
                float shape = Lerp(spiralShape, ring, storm.Organize);
                float add = amount * shape;
                int x = Mod(cx + dx, Size);
                int i = y * Size + x;
                if (storm.Organize > 0.02f && rad < eye)
                {
                    if (cloud[i] > 0.04f)
                        cloud[i] = 0.04f;
                    if (humidity[i] > 0.04f)
                        humidity[i] = 0.04f;
                }
                else
                {
                    if (add > cloud[i])
                        cloud[i] = add;
                    if (add > humidity[i])
                        humidity[i] = add;
                }

                if (cycloneMask != null && storm.Organize > 0.02f && rad >= eye)
                {
                    float mark = ring * storm.Organize;
                    if (mark > cycloneMask[i])
                        cycloneMask[i] = mark;
                }
            }
        }
    }

    static void ClearCycloneMask()
    {
        if (cycloneMask == null || cycloneMask.Length != CellCount)
            cycloneMask = new float[CellCount];
        else
            Array.Clear(cycloneMask, 0, cycloneMask.Length);
    }

    static int CountCyclones()
    {
        if (storms == null)
            return 0;
        int n = 0;
        for (int i = 0; i < storms.Length; i++)
        {
            if (storms[i].Organize >= 0.5f)
                n++;
        }

        return n;
    }

    const int StageCell = 0;
    const int StageSupercell = 1;
    const int StageMeso = 2;
    const int StageFunnel = 3;
    const int StageGround = 4;
    const int StageRope = 5;

    public const int StormObjectFloats = 16;
    public const int StormObjectBytes = 64;
    public const int StormObjectSnapshotBytes = 512;

    struct StormObject
    {
        public Vector3 Position;
        public int Stage;
        public float StageAge;
        public float DemoteAge;
        public float LowFeedAge;
        public int Shape;
        public float SubA;
        public float SubB;
        public float FunnelRadius;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StormObjectRecord
    {
        public float X, Y, Z, Stage, Intensity, Shape, SubA, SubB;
        public float FunnelRadius, StageAge, Organize, Kind, Pad0, Pad1, Pad2, Pad3;
    }

    static void ResetObject(int index)
    {
        if (objects == null || (uint)index >= (uint)objects.Length)
            return;
        objects[index] = default;
        objects[index].Stage = StageCell;
    }

    static void StepObject(int index, Storm storm, CellForce cell, float feed, float dt)
    {
        if (objects == null || (uint)index >= (uint)objects.Length)
            return;
        ref StormObject obj = ref objects[index];
        obj.Position = TexelToPlanet(storm.X, storm.Y);
        if (feed < 0.15f)
            obj.LowFeedAge += dt;
        else
            obj.LowFeedAge = 0f;

        if (obj.Stage == StageGround)
        {
            bool rope = obj.StageAge >= 40f || storm.Intensity < 0.45f || obj.LowFeedAge >= 10f;
            if (rope)
            {
                obj.Stage = StageRope;
                obj.StageAge = 0f;
                return;
            }

            obj.StageAge += dt;
            return;
        }

        if (obj.Stage == StageRope)
        {
            obj.StageAge += dt;
            if (obj.StageAge >= 20f)
            {
                obj.Stage = StageCell;
                obj.StageAge = 0f;
                ClearShape(ref obj);
            }

            return;
        }

        bool weak = feed < 0.12f && cell.Lift < 0.10f && cell.Ocean < 0.20f;
        if (weak)
            obj.DemoteAge += dt;
        else
            obj.DemoteAge = 0f;
        if (obj.Stage != StageCell && obj.DemoteAge >= 15f)
        {
            obj.Stage--;
            obj.StageAge = 0f;
            obj.DemoteAge = 0f;
            if (obj.Stage == StageCell)
                ClearShape(ref obj);
            return;
        }

        if (!StageGate(obj.Stage, storm, cell, feed))
        {
            obj.StageAge = 0f;
            return;
        }

        obj.StageAge += dt;
        float need = obj.Stage == StageCell ? 8f : obj.Stage == StageSupercell ? 12f : obj.Stage == StageMeso ? 8f : 8f;
        if (obj.StageAge < need)
            return;
        int next = obj.Stage + 1;
        obj.Stage = next;
        obj.StageAge = 0f;
        if (next == StageFunnel)
            AssignShape(ref obj, index);
    }

    static bool StageGate(int stage, Storm storm, CellForce cell, float feed)
    {
        if (stage == StageFunnel)
            return true;
        float shear = Math.Abs(Zonal(cell.Thermal));
        if (stage == StageCell)
        {
            return storm.Intensity >= 0.40f
                && feed >= 0.25f
                && cell.Day >= 0.15f
                && shear >= 0.12f;
        }

        if (stage == StageSupercell)
            return storm.Intensity >= 0.55f && shear >= 0.12f;
        if (stage == StageMeso)
            return storm.Intensity >= 0.65f;
        return false;
    }

    static void AssignShape(ref StormObject obj, int index)
    {
        float roll = Hash01(index, planet, 91);
        if (roll < 0.08f)
            obj.Shape = 3;
        else if (roll < 0.50f)
            obj.Shape = 0;
        else if (roll < 0.78f)
            obj.Shape = 1;
        else
            obj.Shape = 2;
        obj.FunnelRadius = obj.Shape == 3 ? 400f : obj.Shape == 1 ? 200f : obj.Shape == 2 ? 160f : 80f;
        float spanA = Lerp(80f, 400f, Hash01(index, planet, 7));
        float spanB = Lerp(80f, 400f, Hash01(index, planet, 9));
        obj.SubA = (Hash01(index, planet, 3) * 2f - 1f) * spanA;
        obj.SubB = (Hash01(index, planet, 5) * 2f - 1f) * spanB;
    }

    static void ClearShape(ref StormObject obj)
    {
        obj.Shape = 0;
        obj.SubA = 0f;
        obj.SubB = 0f;
        obj.FunnelRadius = 0f;
    }

    static Vector3 TexelToPlanet(float texelX, float texelY)
    {
        float u = texelX / Size;
        float v = texelY / Size;
        float lat = (v - 0.5f) * (float)Math.PI;
        float lon = (u - 0.5f) * (float)Math.PI * 2f;
        float cosLat = (float)Math.Cos(lat);
        var dir = new Vector3(cosLat * (float)Math.Sin(lon), (float)Math.Sin(lat), cosLat * (float)Math.Cos(lon));
        float sea = seaRadius > 1f ? seaRadius : 60000f;
        int x = Mod((int)Math.Floor(texelX), Size);
        int y = (int)Math.Floor(texelY);
        if (y < 0)
            y = 0;
        else if (y >= Size)
            y = Size - 1;
        float h = 0f;
        if (height != null)
            h = Math.Max(height[y * Size + x], 0f);
        return dir * (sea + h);
    }

    static void CountStages()
    {
        liveSuper = 0;
        liveTornado = 0;
        if (objects == null)
            return;
        for (int i = 0; i < objects.Length; i++)
        {
            int stage = objects[i].Stage;
            if (stage == StageSupercell || stage == StageMeso)
                liveSuper++;
            else if (stage == StageFunnel || stage == StageGround || stage == StageRope)
                liveTornado++;
        }
    }

    /// <summary>Game thread. Planet orientation for the storm upload. Translation is ignored.</summary>
    public static void SetPlanetBasis(MatrixD world)
    {
        lock (SimLock)
        {
            world.Translation = Vector3D.Zero;
            planetBasis = (Matrix)world;
            planetBasisSet = true;
        }
    }

    /// <summary>Render thread. Camera-relative XYZ plus the rest of each record. 128 floats.</summary>
    public static void WriteCameraStorms(Vector3 centerRel, float[] dst)
    {
        if (dst == null || dst.Length < StormCount * StormObjectFloats)
            return;
        var rec = new StormObjectRecord[StormCount];
        CopyStormObjects(rec);
        Matrix basis;
        bool has;
        lock (SimLock)
        {
            basis = planetBasis;
            has = planetBasisSet;
        }

        for (int i = 0; i < StormCount; i++)
        {
            var local = new Vector3(rec[i].X, rec[i].Y, rec[i].Z);
            var world = has ? Vector3.Transform(local, basis) : local;
            var cam = world + centerRel;
            int o = i * StormObjectFloats;
            dst[o] = cam.X;
            dst[o + 1] = cam.Y;
            dst[o + 2] = cam.Z;
            dst[o + 3] = rec[i].Stage;
            dst[o + 4] = rec[i].Intensity;
            dst[o + 5] = rec[i].Shape;
            dst[o + 6] = rec[i].SubA;
            dst[o + 7] = rec[i].SubB;
            dst[o + 8] = rec[i].FunnelRadius;
            dst[o + 9] = rec[i].StageAge;
            dst[o + 10] = rec[i].Organize;
            dst[o + 11] = rec[i].Kind;
            dst[o + 12] = 1f;
            dst[o + 13] = 0f;
        }
    }

    /// <summary>Planet-local storm records for a later upload. 8 × 64 bytes. Does not touch D3D.</summary>
    public static void CopyStormObjects(StormObjectRecord[] dst)
    {
        if (dst == null || dst.Length < StormCount)
            return;
        lock (SimLock)
        {
            for (int i = 0; i < StormCount; i++)
            {
                StormObject obj = objects != null && i < objects.Length ? objects[i] : default;
                Storm storm = storms != null && i < storms.Length ? storms[i] : default;
                dst[i] = new StormObjectRecord
                {
                    X = obj.Position.X,
                    Y = obj.Position.Y,
                    Z = obj.Position.Z,
                    Stage = obj.Stage,
                    Intensity = storm.Intensity,
                    Shape = obj.Shape,
                    SubA = obj.SubA,
                    SubB = obj.SubB,
                    FunnelRadius = obj.FunnelRadius,
                    StageAge = obj.StageAge,
                    Organize = storm.Organize,
                    Kind = storm.Kind,
                };
            }
        }
    }

    static float Wrap(float x)
    {
        float w = x - (float)Math.Floor(x / Size) * Size;
        return w < 0f ? w + Size : w;
    }

    static float WrapDelta(float d)
    {
        if (d > Size * 0.5f)
            d -= Size;
        else if (d < -Size * 0.5f)
            d += Size;
        return d;
    }

    static void MeasureOcean()
    {
        oceanDatum = 0f;
        oceanFromLowlands = false;
        waterFraction = Mean(waterIce);
        coldFraction = Mean(coldIce);
        materialsFound = waterFraction > 0f || coldFraction > 0f;
        oceanFraction = height == null ? 0.5f : 0f;
        if (materialsFound || height == null)
            return;
        int below = 0;
        for (int i = 0; i < CellCount; i++)
        {
            if (height[i] <= 0f)
                below++;
        }

        oceanFraction = below / (float)CellCount;
        if (oceanFraction >= 0.12f)
            return;

        // EarthLike's shape often stays at or above the sea datum even where
        // the water looks like ocean, so a -180 m test classified the whole
        // grid as land and only the ITCZ stayed above the old condensation cut.
        var sorted = (float[])height.Clone();
        Array.Sort(sorted);
        oceanDatum = sorted[(int)(CellCount * 0.34f)];
        oceanFromLowlands = true;
        int moist = 0;
        for (int i = 0; i < CellCount; i++)
        {
            if (height[i] <= oceanDatum + 350f)
                moist++;
        }

        oceanFraction = moist / (float)CellCount;
    }

    static float FractionAt(float[] field, int x, int y)
    {
        if (field == null || (uint)x >= Size || (uint)y >= Size)
            return 0f;
        return Saturate(field[y * Size + x]);
    }

    static float Mean(float[] field)
    {
        if (field == null || field.Length != CellCount)
            return 0f;
        double sum = 0d;
        for (int i = 0; i < CellCount; i++)
            sum += field[i];
        return (float)(sum / CellCount);
    }

    static float OceanAmount(float heightAboveSea)
    {
        if (height == null)
            return 0.5f;
        if (heightAboveSea <= oceanDatum)
            return 1f;
        return Saturate(1f - (heightAboveSea - oceanDatum) / 350f);
    }

    static float SurfaceColumn(float heightAboveSea)
    {
        if (heightAboveSea <= 0f)
            return 0.02f;
        float elev = Saturate(heightAboveSea / hill);
        return Saturate(0.02f + elev * hillColumn);
    }

    static float Sample(float[] field, float x, float y)
    {
        if (field == null || field.Length < CellCount)
            return 0f;
        if (float.IsNaN(x) || float.IsInfinity(x))
            x = 0f;
        if (float.IsNaN(y) || float.IsInfinity(y))
            y = 0f;

        // Remainder can land on Size itself (float wrap). x0 == Size then
        // indexes CellCount at the south edge and kills the session.
        float xw = x - (float)Math.Floor(x / Size) * Size;
        if (xw < 0f)
            xw += Size;
        if (xw >= Size)
            xw -= Size;
        float yw = y < 0f ? 0f : (y > Size - 1.001f ? Size - 1.001f : y);
        int x0 = (int)xw;
        int y0 = (int)yw;
        if ((uint)x0 >= (uint)Size)
            x0 = 0;
        if ((uint)y0 >= (uint)Size)
            y0 = Size - 1;
        int x1 = x0 + 1;
        if (x1 >= Size)
            x1 = 0;
        int y1 = y0 + 1;
        if (y1 >= Size)
            y1 = y0;
        float tx = xw - x0;
        float ty = yw - y0;
        float a = field[y0 * Size + x0];
        float b = field[y0 * Size + x1];
        float c = field[y1 * Size + x0];
        float d = field[y1 * Size + x1];
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    static void ReadSun(out Vector3 towardSun, out float declination)
    {
        towardSun = Vector3.Zero;
        declination = fallbackDec;
        try
        {
            var sun = -AnomalyScene.SunLightDirection();
            if (sun.LengthSquared() < 1e-6f)
                return;
            sun.Normalize();
            towardSun = sun;
            float dec = (float)Math.Asin(MathHelper.Clamp(Vector3.Dot(sun, axis), -1f, 1f));
            declination = MathHelper.Clamp(dec, -DeclinationCap, DeclinationCap);
        }
        catch (Exception e)
        {
            if (!loggedSunFail)
            {
                loggedSunFail = true;
                MyLog.Default.Warning($"{Plugin.Name}: climate sun read failed: {e.Message}");
            }
        }
    }

    static float Gauss(float x, float center, float width)
    {
        float d = (x - center) / Math.Max(width, 1e-3f);
        return (float)Math.Exp(-d * d);
    }

    static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + seed * 1103515245;
            n = (n ^ (n >> 13)) * 1274126177;
            n ^= n >> 16;
            return (n & 0x7fffffff) / 2147483647f;
        }
    }

    static float Smooth01(float t) => MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(t, 0f, 1f));
    static float Saturate(float v) => MathHelper.Clamp(v, 0f, 1f);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static int Mod(int x, int m)
    {
        int r = x % m;
        return r < 0 ? r + m : r;
    }

    static byte ToByte(float v) => (byte)MathHelper.Clamp(v * 255f, 0f, 255f);
}
