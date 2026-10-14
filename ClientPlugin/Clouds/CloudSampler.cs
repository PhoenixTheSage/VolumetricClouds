using System;
using System.Collections.Generic;
using ClientPlugin.Anomaly;
using Sandbox.Engine.Voxels.Planet;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Clouds;

/// <summary>
/// Game-thread side: nearest planet with CloudLayers, shell radii, fade, albedo.
/// Must never be called from the render thread.
/// </summary>
public static class CloudSampler
{
    const int UpdateInterval = 30;
    const double ExitRangeMargin = 2.0;

    static int frameCounter;
    static bool active;
    static bool dirty = true;
    static long loggedPlanetId;
    static float loggedInner;
    static float loggedOuter;
    static string hudLine;
    static CloudSamplerDiagnostics diagnostics = new();

    /// <summary>Cached HUD/overlay line. HUD draw thread reads this; game thread writes.</summary>
    public static string HudLine => hudLine;

    /// <summary>Last game-thread planet/shell dump for the status screen.</summary>
    public static CloudSamplerDiagnostics Diagnostics => diagnostics;

    public static void Invalidate()
    {
        dirty = true;
    }

    public static void Update()
    {
        if (!dirty && frameCounter++ % UpdateInterval != 0)
            return;
        dirty = false;

        var snap = BuildSnapshot();
        if (snap != null)
            CloudTextures.RequestWeather(snap.PlanetSeed);
        CloudRenderer.Publish(snap);
    }

    /// <summary>
    /// AfterLoading: CPU weather ready before the load screen ends.
    /// Does not need MainCamera. GPU upload stays on the first render tick.
    /// </summary>
    public static void PrepareSessionWeather()
    {
        if (!Config.Current.Enabled)
            return;
        if (MySession.Static == null || MyPlanets.Static == null)
            return;

        var planet = FindNearestPlanet(SessionAnchor());
        if (planet == null)
            return;
        RequestWeatherFor(planet, wait: true);
        dirty = true;
        frameCounter = 0;
        var snap = BuildSnapshot();
        if (snap != null)
            CloudRenderer.Publish(snap);
    }

    public static void OnSessionUnloading()
    {
        active = false;
        dirty = true;
        loggedPlanetId = 0;
        loggedInner = 0f;
        loggedOuter = 0f;
        hudLine = null;
        elevationPlanet = 0;
        elevation = null;
        surfacePlanet = 0;
        waterIce = null;
        coldIce = null;
        CloudClimate.Reset();
        diagnostics = new CloudSamplerDiagnostics { InactiveReason = "session unloading" };
        CloudRenderer.Publish(null);
    }

    static int StableHash(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        unchecked
        {
            int hash = (int)2166136261;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }
            return hash;
        }
    }

    static int WeatherSeed(MyPlanet planet, string weatherTexture)
    {
        int seed = planet.Generator != null
            ? StableHash(planet.Generator.Id.SubtypeName)
            : (int)planet.EntityId;
        if (!string.IsNullOrEmpty(weatherTexture))
            seed ^= StableHash(weatherTexture);
        return seed ^ MixLong(planet.EntityId);
    }

    static int MixLong(long id)
    {
        unchecked
        {
            int a = (int)id;
            int b = (int)(id >> 32);
            return a ^ (b * 16777619);
        }
    }

    static long elevationPlanet;
    static float[] elevation;
    static long surfacePlanet;
    static float[] waterIce;
    static float[] coldIce;

    static void CaptureClimate(MyPlanet planet, int seed)
    {
        CaptureClimate(planet, seed, PlanetAxis(planet), 0.5f);
    }

    static void CaptureClimate(MyPlanet planet, int seed, Vector3 axis, float hillColumn)
    {
        float sea = planet.AverageRadius > 1f ? planet.AverageRadius : planet.MaximumRadius;
        float amp = Math.Max(planet.MaximumRadius - sea, 1f);
        if (elevationPlanet != planet.EntityId || elevation == null)
        {
            elevation = SampleElevation(planet, sea);
            elevationPlanet = planet.EntityId;
        }

        if (surfacePlanet != planet.EntityId || waterIce == null || coldIce == null)
        {
            SampleSurface(planet, sea, out waterIce, out coldIce);
            surfacePlanet = planet.EntityId;
        }

        float spacing = MathHelper.Clamp(Config.Current.Spacing, 0.35f, 3f);
        CloudClimate.SetPlanetBasis(planet.WorldMatrix);
        CloudTextures.SetClimate(seed, SunDeclination(axis), spacing, elevation, amp, axis, hillColumn, waterIce, coldIce, sea);
    }

    static Vector3 PlanetAxis(MyPlanet planet)
    {
        var axis = Vector3.Zero;
        int n = 0;
        var layers = planet.Generator?.CloudLayers;
        if (layers != null)
        {
            foreach (dynamic layer in layers)
            {
                if (layer == null || layer.RotationAxis == Vector3D.Zero)
                    continue;
                axis += (Vector3)Vector3D.Normalize(layer.RotationAxis);
                n++;
            }
        }

        if (n > 0)
            axis /= n;
        if (axis.LengthSquared() < 1e-6f)
            axis = (Vector3)planet.WorldMatrix.Up;
        if (axis.LengthSquared() < 1e-6f)
            axis = Vector3.Up;
        axis.Normalize();
        return axis;
    }

    static float SunDeclination(Vector3 axis)
    {
        try
        {
            var sun = -AnomalyScene.SunLightDirection();
            if (sun.LengthSquared() < 1e-6f || axis.LengthSquared() < 1e-6f)
                return 0f;
            sun.Normalize();
            axis.Normalize();
            float dec = (float)Math.Asin(MathHelper.Clamp(Vector3.Dot(sun, axis), -1f, 1f));
            const float cap = 35f * (float)Math.PI / 180f;
            return MathHelper.Clamp(dec, -cap, cap);
        }
        catch
        {
            return 0f;
        }
    }

    static float[] SampleElevation(MyPlanet planet, float sea)
    {
        int dim = CloudTextures.WeatherSize;
        var height = new float[dim * dim];
        var shape = planet.Provider?.Shape;
        if (shape == null || sea < 1f)
            return height;
        try
        {
            for (int y = 0; y < dim; y++)
            {
                float lat = ((y + 0.5f) / dim - 0.5f) * MathHelper.Pi;
                float cos = (float)Math.Cos(lat);
                float sy = (float)Math.Sin(lat);
                for (int x = 0; x < dim; x++)
                {
                    float lon = ((x + 0.5f) / dim - 0.5f) * MathHelper.TwoPi;
                    var local = new Vector3(cos * (float)Math.Sin(lon), sy, cos * (float)Math.Cos(lon)) * sea;
                    height[y * dim + x] = (float)(-shape.GetDistanceToSurfaceCacheless(local));
                }
            }
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{Plugin.Name}: elevation sample failed: {e.Message}");
        }

        return height;
    }

    /// <summary>
    /// One material read per tap, about a metre inside the surface. Ice with
    /// no snow in a two-texel neighborhood evaporates. Snow, and ice beside
    /// it, stay cold. A storage miss leaves that tap dry.
    /// </summary>
    static void SampleSurface(MyPlanet planet, float sea, out float[] water, out float[] cold)
    {
        int dim = CloudTextures.WeatherSize;
        int n = dim * dim;
        water = new float[n];
        cold = new float[n];
        var shape = planet.Provider?.Shape;
        if (shape == null || sea < 1f || planet.Storage == null)
            return;

        var snow = new float[n];
        var ice = new float[n];
        try
        {
            MatrixD world = planet.WorldMatrix;
            const float tap = 0.32f;
            for (int y = 0; y < dim; y++)
            {
                for (int x = 0; x < dim; x++)
                {
                    int snowTaps = 0;
                    int iceTaps = 0;
                    SampleTap(planet, shape, ref world, sea, dim, x, y, tap, 0f, ref snowTaps, ref iceTaps);
                    SampleTap(planet, shape, ref world, sea, dim, x, y, -tap, 0f, ref snowTaps, ref iceTaps);
                    SampleTap(planet, shape, ref world, sea, dim, x, y, 0f, tap, ref snowTaps, ref iceTaps);
                    SampleTap(planet, shape, ref world, sea, dim, x, y, 0f, -tap, ref snowTaps, ref iceTaps);
                    int i = y * dim + x;
                    snow[i] = snowTaps / 4f;
                    ice[i] = iceTaps / 4f;
                }
            }

            const int radius = 2;
            for (int y = 0; y < dim; y++)
            {
                for (int x = 0; x < dim; x++)
                {
                    bool nearSnow = false;
                    for (int dy = -radius; dy <= radius && !nearSnow; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= dim)
                            continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0)
                                xx += dim;
                            else if (xx >= dim)
                                xx -= dim;
                            if (snow[yy * dim + xx] > 0f)
                            {
                                nearSnow = true;
                                break;
                            }
                        }
                    }

                    int i = y * dim + x;
                    water[i] = nearSnow ? 0f : ice[i];
                    cold[i] = snow[i] + (nearSnow ? ice[i] : 0f);
                }
            }
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{Plugin.Name}: surface material sample failed: {e.Message}");
            Array.Clear(water, 0, water.Length);
            Array.Clear(cold, 0, cold.Length);
        }
    }

    static void SampleTap(
        MyPlanet planet,
        MyPlanetShapeProvider shape,
        ref MatrixD world,
        float sea,
        int dim,
        int x,
        int y,
        float ox,
        float oy,
        ref int snowTaps,
        ref int iceTaps)
    {
        float lat = ((y + 0.5f + oy) / dim - 0.5f) * MathHelper.Pi;
        float lon = ((x + 0.5f + ox) / dim - 0.5f) * MathHelper.TwoPi;
        float cos = (float)Math.Cos(lat);
        var dir = new Vector3(cos * (float)Math.Sin(lon), (float)Math.Sin(lat), cos * (float)Math.Cos(lon));
        float h = (float)(-shape.GetDistanceToSurfaceCacheless(dir * sea));
        float radius = Math.Max(sea + h - 1f, 1f);
        Vector3D local = new Vector3D(dir.X, dir.Y, dir.Z) * radius;
        Vector3D point = Vector3D.Transform(local, world);
        MyVoxelMaterialDefinition mat;
        try
        {
            mat = planet.GetMaterialAt(ref point);
        }
        catch
        {
            return;
        }

        if (IsSnow(mat))
            snowTaps++;
        else if (IsIce(mat))
            iceTaps++;
    }

    static bool IsSnow(MyVoxelMaterialDefinition mat) => NameHas(mat, "Snow");

    static bool IsIce(MyVoxelMaterialDefinition mat) => !IsSnow(mat) && NameHas(mat, "Ice");

    static bool NameHas(MyVoxelMaterialDefinition mat, string token)
    {
        if (mat == null)
            return false;
        if (mat.MaterialTypeName != null && mat.MaterialTypeName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        string sub = mat.Id.SubtypeName;
        return sub != null && sub.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void RequestWeatherFor(MyPlanet planet, bool wait = false)
    {
        if (planet?.Generator == null)
            return;
        string weatherTexture = null;
        var layers = planet.Generator.CloudLayers;
        if (layers != null)
        {
            foreach (dynamic layer in layers)
            {
                if (layer?.Textures == null || layer.Textures.Count == 0)
                    continue;
                weatherTexture = layer.Textures[0];
                break;
            }
        }
        int seed = WeatherSeed(planet, weatherTexture);
        CaptureClimate(planet, seed);
        if (wait)
            CloudTextures.PrepareWeather(seed);
        else
            CloudTextures.RequestWeather(seed);
    }

    static Vector3D SessionAnchor()
    {
        if (MySector.MainCamera != null)
            return MySector.MainCamera.Position;
        var character = MySession.Static?.LocalCharacter;
        if (character != null)
            return character.PositionComp.GetPosition();
        return Vector3D.Zero;
    }

    static MyPlanet FindNearestPlanet(Vector3D from)
    {
        MyPlanet planet = null;
        MyPlanet atmospherePlanet = null;
        double planetDistance = double.MaxValue;
        double atmosphereDistance = double.MaxValue;
        foreach (var candidate in MyPlanets.GetPlanets())
        {
            if (candidate == null || candidate.Closed)
                continue;
            double candidateDistance = from == Vector3D.Zero
                ? 0.0
                : (from - candidate.PositionComp.GetPosition()).Length();
            var layers = candidate.Generator?.CloudLayers;
            if (layers != null && layers.Count > 0 && candidateDistance < planetDistance)
            {
                planetDistance = candidateDistance;
                planet = candidate;
            }

            if (candidate.HasAtmosphere && candidateDistance < atmosphereDistance)
            {
                atmosphereDistance = candidateDistance;
                atmospherePlanet = candidate;
            }
        }

        return planet ?? atmospherePlanet;
    }

    static void SetInactive(string reason)
    {
        active = false;
        hudLine = null;
        diagnostics = new CloudSamplerDiagnostics { InactiveReason = reason };
    }

    static void LogPlanet(MyPlanet planet, float inner, float outer, string detail)
    {
        if (planet.EntityId == loggedPlanetId &&
            Math.Abs(inner - loggedInner) < 40f &&
            Math.Abs(outer - loggedOuter) < 40f)
            return;
        loggedPlanetId = planet.EntityId;
        loggedInner = inner;
        loggedOuter = outer;
        MyLog.Default.Info($"{Plugin.Name}: nearest cloud planet '{planet.Generator?.Id.SubtypeName}': {detail}");
    }

    static CloudSnapshot BuildSnapshot()
    {
        var config = Config.Current;
        if (!config.Enabled)
        {
            SetInactive("config Enabled=false");
            return null;
        }

        if (MySession.Static == null || MyPlanets.Static == null)
        {
            SetInactive("no session / planets");
            return null;
        }

        if (MySector.MainCamera == null)
        {
            SetInactive("no camera");
            return null;
        }

        var cameraPosition = MySector.MainCamera.Position;
        var planet = FindNearestPlanet(cameraPosition);
        if (planet == null)
        {
            SetInactive("no planet with CloudLayers or atmosphere in range");
            return null;
        }
        double planetDistance = (cameraPosition - planet.PositionComp.GetPosition()).Length();

        var center = planet.PositionComp.GetPosition();
        float atmosphere = planet.AtmosphereRadius > 0f ? planet.AtmosphereRadius : planet.MaximumRadius * 1.2f;
        // Leave-planet cull is automatic. Horizon range is view distance, not this.
        const double CameraFadeEndRadii = 12.0;
        double range = atmosphere * (CameraFadeEndRadii + (active ? ExitRangeMargin : 0.0));
        if (planetDistance > range)
        {
            RequestWeatherFor(planet);
            SetInactive($"camera {planetDistance / 1000.0:0.#} km > fade range {range / 1000.0:0.#} km");
            return null;
        }

        active = true;

        DeriveShell(planet, config, out float inner, out float outer, out float visualCeil, out float airTop,
            out bool ceilFromAnomaly, out float terrain, out float sea, out float min01, out float max01,
            out Vector3 albedo, out Vector3 rotationAxis, out float angularVelocity, out float fadeOutStart,
            out float fadeOutEnd, out string weatherTexture,
            out Vector4 layerPeaks, out Vector4 layerWidths, out float terrainHug);

        var up = (Vector3)planet.WorldMatrix.Up;
        up.Normalize();
        if (rotationAxis.LengthSquared() < 1e-6f)
            rotationAxis = up;
        else
            rotationAxis.Normalize();

        float fadeStart = atmosphere * 8f;
        float fadeEnd = atmosphere * 12f;
        float minScaled = (planet.AverageRadius + planet.MaximumRadius) * 0.5f;
        int seed = WeatherSeed(planet, weatherTexture);

        int layerCount = planet.Generator?.CloudLayers?.Count ?? 0;
        LogPlanet(planet, inner, outer,
            $"shell {inner / 1000f:0.#}-{outer / 1000f:0.#} km, sea {sea / 1000f:0.#} km, hills {planet.MaximumRadius / 1000f:0.#} km, ceil {visualCeil / 1000f:0.#} km, layers {layerCount}");
        hudLine = $"{planet.Generator?.Id.SubtypeName ?? "planet"}  {inner / 1000f:0.#}–{outer / 1000f:0.#} km  (sea {sea / 1000f:0.#}, hills {planet.MaximumRadius / 1000f:0.#}, ceil {visualCeil / 1000f:0.#})";
        diagnostics = new CloudSamplerDiagnostics
        {
            Active = true,
            PlanetName = planet.Generator?.Id.SubtypeName ?? "planet",
            CameraDistanceMeters = (float)planetDistance,
            CloudLayerCount = layerCount,
            TerrainRadius = terrain,
            HillRadius = planet.MaximumRadius,
            AverageRadius = planet.AverageRadius,
            SeaRadius = sea,
            AtmosphereRadius = atmosphere,
            AirTop = airTop,
            VisualCeil = visualCeil,
            CeilFromAnomaly = ceilFromAnomaly,
            InnerRadius = inner,
            OuterRadius = outer,
            Min01 = min01,
            Max01 = max01,
            TerrainHug = terrainHug,
            WeatherTexture = weatherTexture,
            LayerPeaks = layerPeaks,
            LayerWidths = layerWidths,
        };

        float airSpan = Math.Max(visualCeil - sea, 200f);
        float hillColumn = MathHelper.Clamp((planet.MaximumRadius - sea) / airSpan, 0.15f, 0.85f);
        CaptureClimate(planet, seed, rotationAxis, hillColumn);
        return new CloudSnapshot(center, inner, outer, up, rotationAxis, angularVelocity, albedo,
            fadeStart, fadeEnd, minScaled, planet.MaximumRadius, terrain, sea, atmosphere, visualCeil, fadeOutStart, fadeOutEnd, seed,
            weatherTexture, layerPeaks, layerWidths, terrainHug);
    }

    static void DeriveShell(MyPlanet planet, Config config,
        out float inner, out float outer, out float visualCeil, out float airTop,
        out bool ceilFromAnomaly, out float terrain, out float sea, out float min01, out float max01,
        out Vector3 albedo, out Vector3 rotationAxis, out float angularVelocity,
        out float fadeOutStart, out float fadeOutEnd, out string weatherTexture,
        out Vector4 layerPeaks, out Vector4 layerWidths, out float terrainHug)
    {
        var colorSum = Vector4.Zero;
        int colorCount = 0;
        rotationAxis = Vector3.Zero;
        angularVelocity = 0f;
        fadeOutStart = 0f;
        fadeOutEnd = 0f;
        weatherTexture = null;
        int axisCount = 0;
        var rels = new List<float>(4);
        var layers = planet.Generator?.CloudLayers;

        if (layers != null)
        {
            foreach (dynamic layer in layers)
            {
                if (layer == null)
                    continue;
            // RelativeAltitude is hill amplitude on Keen CloudSphere. Remap
            // it to 0–1 peaks inside the allowed column (not meters AGL).
            colorSum += layer.Color.ToLinearRGB();
            colorCount++;
            fadeOutStart += layer.FadeOutRelativeAltitudeStart;
            fadeOutEnd += layer.FadeOutRelativeAltitudeEnd;
            if (rels.Count < 4)
                rels.Add(layer.RelativeAltitude);
            if (weatherTexture == null && layer.Textures != null && layer.Textures.Count > 0)
                weatherTexture = layer.Textures[0];
            if (layer.RotationAxis != Vector3D.Zero)
            {
                rotationAxis += (Vector3)Vector3D.Normalize(layer.RotationAxis);
                axisCount++;
            }
            angularVelocity += layer.AngularVelocity;
            }
        }

        float hillTop = planet.MaximumRadius;
        float avg = planet.AverageRadius;
        terrain = planet.MinimumRadius > avg * 0.5f ? planet.MinimumRadius : avg;
        var center = planet.PositionComp.GetPosition();
        // Slice AK extras when this planet matches Anomaly's snapshot.
        // Fail closed to the same formula as PlanetAtmosphere.TryComputeRadii
        // (air top, not 0.90× / 0.72× AtmosphereRadius).
        ceilFromAnomaly = AnomalyBridge.TryGetCeilings(center, out airTop, out visualCeil) &&
            visualCeil > terrain + 80f;
        if (!ceilFromAnomaly)
            ComputeCeilings(planet, avg, terrain, hillTop, out airTop, out visualCeil);

        // 0 = Keen AverageRadius (Shape.Radius = datum / sea). Hills are
        // radius + HillParams.Max. Valleys (MinimumRadius) stay open so Low
        // can sit around mountains; GBuffer occludes the mesh.
        sea = avg > terrain * 0.5f && avg > 1f ? avg : terrain;
        min01 = MathHelper.Clamp(config.BaseAltitude, 0f, 5f);
        max01 = MathHelper.Clamp(config.MaxAltitude, 0f, 5f);
        if (max01 < min01 + 0.05f)
            max01 = min01 + 0.05f;
        // One unit for both sliders. 0 = sea. 1 = lerp(hills, visual ceil, fit),
        // the visible limb (Slice AP: gameplay ceil is past the blue rim).
        // Above 1 continues past that lid in the same metres. Do not map Min
        // through the full ceil and then crush only Max onto the limb —
        // those two spheres collapse into a lit shell.
        float span = Math.Max(visualCeil - sea, 200f);
        float hHill = MathHelper.Clamp((hillTop - sea) / span, 0f, 1f);
        float fit = MathHelper.Clamp(config.AtmosphereFit, 0.35f, 1f);
        float unit = span * MathHelper.Lerp(hHill, 1f, fit);
        inner = sea + unit * min01;
        outer = sea + unit * max01;
        if (outer < inner + 80f)
            outer = inner + 200f;
        if (inner >= outer)
        {
            inner = terrain;
            outer = visualCeil;
        }

        terrainHug = inner <= hillTop + 50f ? 1f : 0f;
        BuildLayerBands(rels, out layerPeaks, out layerWidths);

        albedo = colorCount > 0
            ? new Vector3(colorSum.X, colorSum.Y, colorSum.Z) / colorCount
            : Vector3.One;
        if (albedo.LengthSquared() < 1e-4f)
            albedo = Vector3.One;
        if (axisCount > 0)
            rotationAxis /= axisCount;
        int layerCount = layers?.Count ?? 0;
        if (layerCount > 0)
        {
            angularVelocity /= layerCount;
            fadeOutStart /= layerCount;
            fadeOutEnd /= layerCount;
        }
    }

    // Same as ClientPlugin.Shaders.PlanetAtmosphere.TryComputeRadii.
    static void ComputeCeilings(MyPlanet planet, float avg, float terrain, float hill,
        out float airTop, out float visualCeil)
    {
        if (avg < terrain * 0.5f)
            avg = terrain;
        var airColumn = planet.AtmosphereAltitude;
        if (airColumn < 200f)
            airColumn = Math.Max(hill - avg, 200f);
        airTop = avg + airColumn;
        visualCeil = airTop;
        if (visualCeil <= terrain + 80f)
            visualCeil = terrain + 200f;
    }

    static void BuildLayerBands(List<float> rels, out Vector4 peaks, out Vector4 widths)
    {
        peaks = Vector4.Zero;
        widths = Vector4.Zero;
        int n = rels == null ? 0 : rels.Count;
        if (n <= 0)
        {
            peaks.X = 0.42f;
            widths.X = 0.55f;
            return;
        }

        var sorted = rels.ToArray();
        Array.Sort(sorted);
        float rMin = sorted[0];
        float rMax = sorted[n - 1];
        float span = rMax - rMin;
        for (int i = 0; i < n; i++)
        {
            float p = span < 1e-4f
                ? (n == 1 ? 0.42f : i / (float)(n - 1))
                : (sorted[i] - rMin) / span;
            float prev = i > 0
                ? (span < 1e-4f ? (i - 1) / (float)(n - 1) : (sorted[i - 1] - rMin) / span)
                : p;
            float next = i < n - 1
                ? (span < 1e-4f ? (i + 1) / (float)(n - 1) : (sorted[i + 1] - rMin) / span)
                : p;
            float gap = Math.Max(p - prev, next - p);
            float w = Math.Max(0.22f, 0.55f * gap + 0.12f);
            switch (i)
            {
                case 0:
                    peaks.X = p;
                    widths.X = w;
                    break;
                case 1:
                    peaks.Y = p;
                    widths.Y = w;
                    break;
                case 2:
                    peaks.Z = p;
                    widths.Z = w;
                    break;
                default:
                    peaks.W = p;
                    widths.W = w;
                    break;
            }
        }
    }
}
