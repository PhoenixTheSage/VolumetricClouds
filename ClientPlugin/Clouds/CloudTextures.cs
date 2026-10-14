using System;
using System.IO;
using System.Threading.Tasks;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Clouds;

public interface ICloudSrv
{
    string Name { get; }
    int Width { get; }
    int Height { get; }
    int Depth { get; }
    byte[] Rgba { get; }
    byte[][] Mips { get; }
    float[] Structured { get; }
    int Stride { get; }
}

/// <summary>Eight storm records, four float4s each. Pack slot t12.</summary>
sealed class CloudStormBuffer : ICloudSrv, IDisposable
{
    float[] values = Array.Empty<float>();

    public string Name { get; }
    public int Width => values.Length;
    public int Height => 1;
    public int Depth => 1;
    public byte[] Rgba => null;
    public byte[][] Mips => null;
    public float[] Structured => values;
    public int Stride => 16;
    public Vector3I Size3 => new Vector3I(32, 1, 1);

    public CloudStormBuffer(string name) => Name = name;

    public void Update(float[] data)
    {
        if (data == null || data.Length < 128)
            return;
        values = (float[])data.Clone();
    }

    public void Dispose() => values = Array.Empty<float>();
}

/// <summary>Plugin-owned immutable 3D noise volume.</summary>
sealed class CloudTexture3D : ICloudSrv, IDisposable
{
    readonly Vector3I size3;
    readonly int mipLevels;
    byte[] rgba;
    byte[][] mips;

    public string Name { get; }
    public bool CanUpdate { get; }
    public int Width => size3.X;
    public int Height => size3.Y;
    public int Depth => size3.Z;
    public byte[] Rgba => rgba;
    public byte[][] Mips => mips;
    public float[] Structured => null;
    public int Stride => 0;
    public Vector3I Size3 => size3;
    public int MipLevels => mipLevels;

    public CloudTexture3D(string name, int width, int height, int depth, byte[] rgbaPixels, bool updatable = false)
    {
        Name = name;
        CanUpdate = updatable;
        size3 = new Vector3I(width, height, depth);
        mipLevels = 1;
        rgba = rgbaPixels;
        mips = rgbaPixels == null ? null : new[] { rgbaPixels };
    }

    /// <summary>Replaces the weather image. The catalog publish copies these bytes.</summary>
    public void UpdateRgba(byte[] rgbaPixels)
    {
        if (!CanUpdate || rgbaPixels == null)
            return;
        rgba = rgbaPixels;
        mips = new[] { rgbaPixels };
    }

    /// <summary>RGBA volume with CPU box-filter mips (weather LOD).</summary>
    public CloudTexture3D(string name, int dim, byte[] rgbaPixels, int mipCount)
    {
        Name = name;
        size3 = new Vector3I(dim, dim, dim);
        mips = BuildBoxMips(rgbaPixels, dim, mipCount);
        mipLevels = mips.Length;
        rgba = mips.Length > 0 ? mips[0] : null;
    }

    static byte[][] BuildBoxMips(byte[] top, int dim, int mipCount)
    {
        mipCount = Math.Max(mipCount, 1);
        var mips = new byte[mipCount][];
        mips[0] = top;
        int size = dim;
        for (int i = 1; i < mipCount; i++)
        {
            mips[i] = BoxFilter3D(mips[i - 1], size);
            size /= 2;
        }
        return mips;
    }

    static byte[] BoxFilter3D(byte[] src, int dim)
    {
        int half = Math.Max(dim / 2, 1);
        var dst = new byte[half * half * half * 4];
        for (int z = 0; z < half; z++)
        for (int y = 0; y < half; y++)
        for (int x = 0; x < half; x++)
        {
            int r = 0, g = 0, b = 0, a = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int sx = (x * 2 + dx) & (dim - 1);
                int sy = (y * 2 + dy) & (dim - 1);
                int sz = (z * 2 + dz) & (dim - 1);
                int i = ((sz * dim + sy) * dim + sx) * 4;
                r += src[i];
                g += src[i + 1];
                b += src[i + 2];
                a += src[i + 3];
            }
            int o = ((z * half + y) * half + x) * 4;
            dst[o] = (byte)(r >> 3);
            dst[o + 1] = (byte)(g >> 3);
            dst[o + 2] = (byte)(b >> 3);
            dst[o + 3] = (byte)(a >> 3);
        }
        return dst;
    }

    public void Dispose()
    {
        rgba = null;
        mips = null;
    }
}

/// <summary>
/// Owns AutoPBR shape/detail volumes and a 2D weather coverage map
/// (Texture3D W×H×1 — catalog has no Texture2D). Recreated after device
/// reset via Anomaly <c>RegisterLifetime</c>.
/// </summary>
public static class CloudTextures
{
    const int ShapeSize = 128;
    const int DetailSize = 64;
    public const int WeatherSize = 64;
    // HZD / Evolved 128³ wrap. Bake Worley-4 → puff diameter ≈ period/4 ≈ 1 km.
    public const float EvolvedShapePeriodMetres = 4000f;
    // Cubed GetUprezzed: inSamplePosition * 0.01.
    public const float CubedUprezPeriodMetres = 100f;
    const string ShapeFile = "cloud_noise_shape_128_v5.bin";
    const string DetailFile = "cloud_noise_detail_64_v5.bin";

    static readonly object CreateLock = new();
    static readonly byte[] UploadScratch = new byte[CloudClimate.VolumeBytes];
    static string packRoot;
    static CloudTexture3D shape;
    static CloudTexture3D detail;
    static CloudTexture3D weather;
    static CloudStormBuffer storms;
    static readonly float[] stormUpload = new float[128];
    static float boltAge = 1f;
    static float boltCooldown;
    static float boltSeed = 1f;
    static int boltStorm = -1;
    static int weatherSeed = int.MinValue;
    static byte[] weatherCpuPixels;
    static bool loggedUploadFail;

    internal static CloudTexture3D Shape => shape;
    internal static CloudTexture3D Detail => detail;
    internal static CloudTexture3D Weather => weather;
    internal static CloudStormBuffer Storms => storms;

    public static string StatusLine()
    {
        lock (CreateLock)
        {
            string Dim3(CloudTexture3D tex) =>
                tex == null ? "missing" : $"{tex.Size3.X}³/{tex.MipLevels}mip";
            string weatherState = weather != null
                ? $"{weather.Size3.X}×{weather.Size3.Y}×{weather.Size3.Z} live · {CloudClimate.MoistureLine}"
                : weatherCpuPixels != null
                    ? "upload pending"
                    : "missing";
            string sim = CloudClimate.Running
                ? $" · sim {CloudClimate.Steps} steps"
                : " · sim idle";
            return
                $"shape {Dim3(shape)} · detail {Dim3(detail)} · weather {weatherState}{sim} · noise v5" +
                (string.IsNullOrEmpty(packRoot) ? " · packRoot unset" : "");
        }
    }

    public static void SetPackRoot(string root) => packRoot = root;

    public static void Invalidate()
    {
        lock (CreateLock)
        {
            shape?.Dispose();
            detail?.Dispose();
            weather?.Dispose();
            storms?.Dispose();
            shape = null;
            detail = null;
            weather = null;
            storms = null;
            weatherSeed = int.MinValue;
            weatherCpuPixels = null;
        }

        CloudClimate.Reset();
    }

    public static bool WeatherReady(int planetSeed)
    {
        lock (CreateLock)
            return weather != null && weatherSeed == planetSeed && CloudClimate.Running;
    }

    /// <summary>
    /// Game thread. Caches elevation and spin axis, and seeds the live field
    /// when the planet changes. Does not write the grid to disk.
    /// </summary>
    public static void SetClimate(int planetSeed, float declinationRad, float spacing, float[] heightAboveSea, float hillAmplitude, VRageMath.Vector3 axis, float hillColumn = 0.5f, float[] waterIce = null, float[] coldIce = null, float seaMetres = 0f)
    {
        var spin = axis.LengthSquared() > 1e-6f ? VRageMath.Vector3.Normalize(axis) : VRageMath.Vector3.Up;
        CloudClimate.Bind(planetSeed, declinationRad, spacing, heightAboveSea, hillAmplitude, spin, hillColumn, waterIce, coldIce, seaMetres);
        lock (CreateLock)
            weatherCpuPixels = CloudClimate.CopyPixels();
    }

    /// <summary>Game thread. Advances the moisture field about four times a second.</summary>
    public static void StepClimate()
    {
        float dt = 1f / 60f;
        try
        {
            dt = (float)AnomalyScene.FrameSeconds();
        }
        catch
        {
            // Environment clock is not up yet. Step with a nominal frame.
        }

        if (!CloudClimate.Step(dt))
            return;
        lock (CreateLock)
            weatherCpuPixels = CloudClimate.CopyPixels();
    }

    /// <summary>GPU weather image is up. The field itself keeps stepping.</summary>
    public static bool HasWeather
    {
        get
        {
            lock (CreateLock)
                return weather != null;
        }
    }

    /// <summary>Session load. The sim is already seeded by <see cref="SetClimate"/>.</summary>
    public static void PrepareWeather(int planetSeed)
    {
        if (CloudClimate.PlanetSeed != planetSeed)
            return;
        lock (CreateLock)
        {
            if (weatherCpuPixels == null)
                weatherCpuPixels = CloudClimate.CopyPixels();
        }
    }

    /// <summary>Game thread. Does not bake or write a weather file.</summary>
    public static void RequestWeather(int planetSeed) => PrepareWeather(planetSeed);

    /// <summary>Render thread. Shape and detail only. The studio does not need a planet.</summary>
    public static bool EnsureNoise()
    {
        lock (CreateLock)
        {
            if (shape == null)
                shape = LoadOrGenerateVolume("CloudsShape", ShapeFile, ShapeSize, 8, out _);
            if (detail == null)
                detail = LoadOrGenerateVolume("CloudsDetail", DetailFile, DetailSize, 7, out _);
            return shape != null && detail != null;
        }
    }

    /// <summary>Render thread: shape/detail from disk, weather image created once.</summary>
    public static void EnsureCreated(int planetSeed, string weatherTexture = null)
    {
        if (shape != null && detail != null && weather != null && storms != null && weather.CanUpdate
            && weather.Size3.Z == 2 && weatherSeed == planetSeed)
            return;

        lock (CreateLock)
        {
            if (shape == null)
                shape = LoadOrGenerateVolume("CloudsShape", ShapeFile, ShapeSize, 8, out _);
            if (detail == null)
                detail = LoadOrGenerateVolume("CloudsDetail", DetailFile, DetailSize, 7, out _);
            if (storms == null)
                storms = new CloudStormBuffer("CloudsStorms");
            if (weather != null && weather.CanUpdate && weather.Size3.Z == 2 && weatherSeed == planetSeed)
                return;

            if (!CloudClimate.CopyVolume(UploadScratch, false))
                return;
            if (weather != null && weather.CanUpdate && weather.Size3.Z == 2)
            {
                weather.UpdateRgba(UploadScratch);
                weatherSeed = planetSeed;
                StoreCpuSlice();
                CloudClimate.ClearDirty();
                return;
            }

            weather?.Dispose();
            // Slice 0 is the live map. Slice 1 is the surface fraction.
            weather = new CloudTexture3D("CloudsWeather", WeatherSize, WeatherSize, 2, UploadScratch, updatable: true);
            weatherSeed = planetSeed;
            StoreCpuSlice();
            CloudClimate.ClearDirty();
        }
    }

    static void StoreCpuSlice()
    {
        if (weatherCpuPixels == null || weatherCpuPixels.Length != WeatherSize * WeatherSize * 4)
            weatherCpuPixels = new byte[WeatherSize * WeatherSize * 4];
        System.Buffer.BlockCopy(UploadScratch, 0, weatherCpuPixels, 0, weatherCpuPixels.Length);
    }

    static void StampBolt()
    {
        float dt = 1f / 60f;
        try
        {
            dt = (float)AnomalyScene.FrameSeconds();
        }
        catch
        {
            // Render clock is not up yet.
        }

        if (dt < 0f || dt > 0.5f)
            dt = 1f / 60f;
        boltAge += dt;
        boltCooldown -= dt;
        if (boltAge > 0.15f)
            boltStorm = -1;
        if (boltStorm < 0 && boltCooldown <= 0f)
        {
            int best = -1;
            float bestD = 2000f;
            for (int i = 0; i < 8; i++)
            {
                int o = i * 16;
                if (stormUpload[o + 3] < 0.5f)
                    continue;
                float x = stormUpload[o];
                float y = stormUpload[o + 1];
                float z = stormUpload[o + 2];
                float d = (float)Math.Sqrt(x * x + y * y + z * z);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best >= 0)
            {
                boltStorm = best;
                boltAge = 0f;
                boltSeed += 17.13f;
                boltCooldown = 1f;
            }
        }

        if (boltStorm < 0)
            return;
        int at = boltStorm * 16;
        stormUpload[at + 12] = boltAge;
        stormUpload[at + 13] = boltSeed;
    }

    /// <summary>Render thread. Camera-relative storm records for t12.</summary>
    public static void UploadStorms(VRageMath.Vector3 centerRel)
    {
        CloudClimate.WriteCameraStorms(centerRel, stormUpload);
        StampBolt();
        lock (CreateLock)
        {
            if (storms == null)
                return;
            try
            {
                storms.Update(stormUpload);
            }
            catch (Exception e)
            {
                if (loggedUploadFail)
                    return;
                loggedUploadFail = true;
                MyLog.Default.Warning($"{Plugin.Name}: storm upload failed: {e.Message}");
            }
        }
    }

    /// <summary>Render thread. Uploads the latest sim image. No disk write.</summary>
    public static void UploadLiveWeather()
    {
        if (!CloudClimate.CopyVolume(UploadScratch, true))
            return;

        lock (CreateLock)
        {
            StoreCpuSlice();
            if (weather == null || !weather.CanUpdate)
                return;
            try
            {
                weather.UpdateRgba(UploadScratch);
            }
            catch (Exception e)
            {
                if (loggedUploadFail)
                    return;
                loggedUploadFail = true;
                MyLog.Default.Warning($"{Plugin.Name}: weather upload failed: {e.Message}");
            }
        }
    }

    static CloudTexture3D LoadOrGenerateVolume(
        string name, string fileName, int dim, int mipCount, out byte[] cpu)
    {
        var expected = dim * dim * dim * 4;
        if (!string.IsNullOrEmpty(packRoot))
        {
            var path = Path.Combine(packRoot, "Noise", fileName);
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == expected)
                {
                    cpu = bytes;
                    return new CloudTexture3D(name, dim, bytes, mipCount);
                }
                MyLog.Default.Warning($"{Plugin.Name}: {fileName} length {bytes.Length}, expected {expected}");
            }
            else
                MyLog.Default.Warning($"{Plugin.Name}: missing {path}, generating fallback noise");
        }

        cpu = GenerateVolume(dim, name.GetHashCode());
        return new CloudTexture3D(name, dim, cpu, mipCount);
    }

    static byte[] GenerateVolume(int dim, int seed)
    {
        var pixels = new byte[dim * dim * dim * 4];
        Parallel.For(0, dim, z =>
        {
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                float u = x / (float)dim;
                float v = y / (float)dim;
                float w = z / (float)dim;
                int i = ((z * dim + y) * dim + x) * 4;
                pixels[i] = ToByte(FbmWrap3(u * 4f, v * 4f, w * 4f, seed, 4f));
                pixels[i + 1] = ToByte(FbmWrap3(u * 8f + 0.3f, v * 8f, w * 8f, seed + 11, 8f));
                pixels[i + 2] = ToByte(FbmWrap3(u * 16f, v * 16f + 0.5f, w * 16f, seed + 29, 16f));
                pixels[i + 3] = ToByte(FbmWrap3(u * 32f, v * 32f, w * 32f + 0.7f, seed + 47, 32f));
            }
        });
        return pixels;
    }

    /// <summary>Bilinear wrap sample of the CPU 2D weather map. Worker-safe.</summary>
    public static bool TrySampleWeather(float u, float v, out Vector4 rgba)
    {
        byte[] pixels;
        lock (CreateLock)
            pixels = weatherCpuPixels;
        if (pixels == null || pixels.Length < WeatherSize * WeatherSize * 4)
        {
            rgba = Vector4.Zero;
            return false;
        }

        rgba = SampleWeatherPixels(pixels, u, v);
        return true;
    }

    internal static byte[] CopyCpuWeather()
    {
        lock (CreateLock)
        {
            if (weatherCpuPixels == null)
                return null;
            return (byte[])weatherCpuPixels.Clone();
        }
    }

    internal static Vector4 SampleWeatherPixels(byte[] pixels, float u, float v)
    {
        u = u - (float)Math.Floor(u);
        v = v - (float)Math.Floor(v);
        if (u < 0f) u += 1f;
        if (v < 0f) v += 1f;
        float x = u * WeatherSize;
        float y = v * WeatherSize;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        float tx = x - x0;
        float ty = y - y0;
        int x1 = ((x0 + 1) % WeatherSize + WeatherSize) % WeatherSize;
        int y1 = ((y0 + 1) % WeatherSize + WeatherSize) % WeatherSize;
        x0 = ((x0 % WeatherSize) + WeatherSize) % WeatherSize;
        y0 = ((y0 % WeatherSize) + WeatherSize) % WeatherSize;
        Vector4 a = WeatherTexel(pixels, x0, y0);
        Vector4 b = WeatherTexel(pixels, x1, y0);
        Vector4 c = WeatherTexel(pixels, x0, y1);
        Vector4 d = WeatherTexel(pixels, x1, y1);
        return Vector4.Lerp(Vector4.Lerp(a, b, tx), Vector4.Lerp(c, d, tx), ty);
    }

    static Vector4 WeatherTexel(byte[] pixels, int x, int y)
    {
        int i = (y * WeatherSize + x) * 4;
        return new Vector4(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) * (1f / 255f);
    }

    static float FbmWrap3(float x, float y, float z, int seed, float period, int octaves = 5)
    {
        float sum = 0f;
        float amp = 0.5f;
        float freq = 1f;
        float p = period;
        int n = Math.Max(octaves, 1);
        for (int o = 0; o < n; o++)
        {
            sum += ValueNoiseWrap3(x * freq, y * freq, z * freq, seed + o * 101, p) * amp;
            freq *= 2.02f;
            p *= 2.02f;
            amp *= 0.5f;
        }

        return MathHelper.Clamp(sum, 0f, 1f);
    }

    static float ValueNoiseWrap3(float x, float y, float z, int seed, float period)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int z0 = (int)Math.Floor(z);
        float tx = x - x0;
        float ty = y - y0;
        float tz = z - z0;
        int px = Math.Max((int)Math.Round(period), 1);
        float n000 = Hash013(Mod(x0, px), Mod(y0, px), Mod(z0, px), seed);
        float n100 = Hash013(Mod(x0 + 1, px), Mod(y0, px), Mod(z0, px), seed);
        float n010 = Hash013(Mod(x0, px), Mod(y0 + 1, px), Mod(z0, px), seed);
        float n110 = Hash013(Mod(x0 + 1, px), Mod(y0 + 1, px), Mod(z0, px), seed);
        float n001 = Hash013(Mod(x0, px), Mod(y0, px), Mod(z0 + 1, px), seed);
        float n101 = Hash013(Mod(x0 + 1, px), Mod(y0, px), Mod(z0 + 1, px), seed);
        float n011 = Hash013(Mod(x0, px), Mod(y0 + 1, px), Mod(z0 + 1, px), seed);
        float n111 = Hash013(Mod(x0 + 1, px), Mod(y0 + 1, px), Mod(z0 + 1, px), seed);
        float x00 = MathHelper.SmoothStep(n000, n100, tx);
        float x10 = MathHelper.SmoothStep(n010, n110, tx);
        float x01 = MathHelper.SmoothStep(n001, n101, tx);
        float x11 = MathHelper.SmoothStep(n011, n111, tx);
        float y0v = MathHelper.SmoothStep(x00, x10, ty);
        float y1v = MathHelper.SmoothStep(x01, x11, ty);
        return MathHelper.SmoothStep(y0v, y1v, tz);
    }

    static int Mod(int x, int m)
    {
        int r = x % m;
        return r < 0 ? r + m : r;
    }

    static float Hash013(int x, int y, int z, int seed)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + z * 1274126177 + seed * 1103515245;
            n = (n ^ (n >> 13)) * 1274126177;
            n ^= n >> 16;
            return (n & 0x7fffffff) / 2147483647f;
        }
    }

    static byte ToByte(float v) => (byte)MathHelper.Clamp(v * 255f, 0f, 255f);
}
