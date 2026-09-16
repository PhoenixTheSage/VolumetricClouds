using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Clouds;

interface ICloudSrv : ISrvBindable
{
    new SharpDX.Direct3D11.Resource Resource { get; }
}

/// <summary>Plugin-owned immutable 2D texture for the weather map.</summary>
sealed class CloudTexture2D : ICloudSrv, IDisposable
{
    readonly Texture2D texture;
    readonly ShaderResourceView srv;
    readonly Vector2I size;

    public string Name { get; }
    public SharpDX.Direct3D11.Resource Resource => texture;
    public ShaderResourceView Srv => srv;
    public Vector2I Size => size;
    public Vector3I Size3 => new Vector3I(size.X, size.Y, 1);

    public CloudTexture2D(string name, int width, int height, byte[] rgbaPixels)
    {
        Name = name;
        size = new Vector2I(width, height);
        var desc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };
        var handle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
        try
        {
            var data = new DataRectangle(handle.AddrOfPinnedObject(), width * 4);
            texture = new Texture2D(MyRender11.DeviceInstance, desc, new[] { data });
        }
        finally
        {
            handle.Free();
        }

        texture.DebugName = name;
        srv = new ShaderResourceView(MyRender11.DeviceInstance, texture);
    }

    public void Dispose()
    {
        srv.Dispose();
        texture.Dispose();
    }
}

/// <summary>Plugin-owned immutable 3D noise volume.</summary>
sealed class CloudTexture3D : ICloudSrv, IDisposable
{
    readonly Texture3D texture;
    readonly ShaderResourceView srv;
    readonly Vector3I size3;

    public string Name { get; }
    public SharpDX.Direct3D11.Resource Resource => texture;
    public ShaderResourceView Srv => srv;
    public Vector2I Size => new Vector2I(size3.X, size3.Y);
    public Vector3I Size3 => size3;

    public CloudTexture3D(string name, int width, int height, int depth, byte[] rgbaPixels)
    {
        Name = name;
        size3 = new Vector3I(width, height, depth);
        var desc = new Texture3DDescription
        {
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = 1,
            Format = Format.R8G8B8A8_UNorm,
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };
        var handle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
        try
        {
            var box = new DataBox(handle.AddrOfPinnedObject(), width * 4, width * height * 4);
            texture = new Texture3D(MyRender11.DeviceInstance, desc, new[] { box });
        }
        finally
        {
            handle.Free();
        }

        texture.DebugName = name;
        srv = new ShaderResourceView(MyRender11.DeviceInstance, texture);
    }

    public void Dispose()
    {
        srv.Dispose();
        texture.Dispose();
    }
}

/// <summary>
/// Owns AutoPBR shape/detail volumes and a seeded weather map.
/// Recreated after device reset via Anomaly <c>RegisterLifetime</c>.
/// </summary>
public static class CloudTextures
{
    const int ShapeSize = 128;
    const int DetailSize = 64;
    const int WeatherSize = 64;
    const int WeatherRev = 9;
    const string ShapeFile = "cloud_noise_shape_128_v2.bin";
    const string DetailFile = "cloud_noise_detail_64_v2.bin";

    static readonly object CreateLock = new();
    static string packRoot;
    static CloudTexture3D shape;
    static CloudTexture3D detail;
    static CloudTexture3D weather;
    static int weatherSeed = int.MinValue;

    internal static CloudTexture3D Shape => shape;
    internal static CloudTexture3D Detail => detail;
    internal static ISrvBindable Weather => weather;

    public static void SetPackRoot(string root)
    {
        packRoot = root;
    }

    public static void Invalidate()
    {
        lock (CreateLock)
        {
            shape?.Dispose();
            detail?.Dispose();
            weather?.Dispose();
            shape = null;
            detail = null;
            weather = null;
            weatherSeed = int.MinValue;
        }
    }

    public static void EnsureCreated(int planetSeed)
    {
        if (shape != null && detail != null && weather != null && weatherSeed == (planetSeed ^ WeatherRev))
            return;

        lock (CreateLock)
        {
            if (shape == null)
                shape = LoadOrGenerateVolume("CloudsShape", ShapeFile, ShapeSize);
            if (detail == null)
                detail = LoadOrGenerateVolume("CloudsDetail", DetailFile, DetailSize);
            if (weather == null || weatherSeed != (planetSeed ^ WeatherRev))
            {
                weather?.Dispose();
                weather = CreateWeather(planetSeed);
                weatherSeed = planetSeed ^ WeatherRev;
            }
        }
    }

    static CloudTexture3D LoadOrGenerateVolume(string name, string fileName, int dim)
    {
        var expected = dim * dim * dim * 4;
        if (!string.IsNullOrEmpty(packRoot))
        {
            var path = Path.Combine(packRoot, "Noise", fileName);
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == expected)
                    return new CloudTexture3D(name, dim, dim, dim, bytes);
                MyLog.Default.Warning($"{Plugin.Name}: {fileName} length {bytes.Length}, expected {expected}");
            }
            else
            {
                MyLog.Default.Warning($"{Plugin.Name}: missing {path}, generating fallback noise");
            }
        }

        return new CloudTexture3D(name, dim, dim, dim, GenerateVolume(dim, name.GetHashCode()));
    }

    static byte[] GenerateVolume(int dim, int seed)
    {
        var pixels = new byte[dim * dim * dim * 4];
        for (int z = 0; z < dim; z++)
        for (int y = 0; y < dim; y++)
        for (int x = 0; x < dim; x++)
        {
            int i = ((z * dim + y) * dim + x) * 4;
            pixels[i] = HashByte(x, y, z, seed);
            pixels[i + 1] = HashByte(x + 17, y + 9, z + 3, seed + 11);
            pixels[i + 2] = HashByte(x + 5, y + 23, z + 13, seed + 29);
            pixels[i + 3] = HashByte(x + 31, y + 7, z + 19, seed + 47);
        }

        return pixels;
    }

    static CloudTexture3D CreateWeather(int seed)
    {
        var dim = WeatherSize;
        var pixels = new byte[dim * dim * dim * 4];
        // Wrapping 3D FBM: continuous on a spherical shell. 2D lat/lon
        // pinched at poles; 2D triplanar cut great-circle cube faces.
        for (int z = 0; z < dim; z++)
        for (int y = 0; y < dim; y++)
        for (int x = 0; x < dim; x++)
        {
            float u = x / (float)dim;
            float v = y / (float)dim;
            float w = z / (float)dim;
            float large = FbmWrap3(u * 6f, v * 6f, w * 6f, seed, 6f);
            float medium = FbmWrap3(u * 14f + 2.1f, v * 14f + 1.3f, w * 14f + 0.7f, seed + 19, 14f);
            float fine = FbmWrap3(u * 28f + 5.7f, v * 28f + 3.4f, w * 28f + 1.9f, seed + 71, 28f);
            float n = large * 0.65f + medium * 0.35f;
            float coverage = Smooth01((n - 0.32f) / 0.38f);
            coverage = MathHelper.Clamp(coverage + (fine - 0.5f) * 0.22f, 0f, 1f);

            float type = FbmWrap3(u * 3.4f + 1.1f, v * 3.4f + 0.6f, w * 3.4f + 0.4f, seed + 131, 3.4f);
            type = Smooth01((type - 0.22f) / 0.55f);
            float potential = FbmWrap3(u * 9f + 8.2f, v * 9f + 4.7f, w * 9f + 2.3f, seed + 173, 9f);
            float convection = Smooth01(
                (FbmWrap3(u * 5f + 3.3f, v * 5f + 1.8f, w * 5f + 0.9f, seed + 251, 5f) - 0.42f) / 0.38f);
            convection *= Smooth01((coverage - 0.35f) / 0.25f);

            int i = ((z * dim + y) * dim + x) * 4;
            pixels[i] = ToByte(coverage);
            pixels[i + 1] = ToByte(type);
            pixels[i + 2] = ToByte(potential);
            pixels[i + 3] = ToByte(convection);
        }

        return new CloudTexture3D("CloudsWeather", dim, dim, dim, pixels);
    }

    static float Smooth01(float t) => MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(t, 0f, 1f));

    static float FbmWrap3(float x, float y, float z, int seed, float period)
    {
        float sum = 0f;
        float amp = 0.5f;
        float freq = 1f;
        float p = period;
        for (int o = 0; o < 5; o++)
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

    static byte HashByte(int x, int y, int z, int seed)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + z * 1274126177 + seed;
            n = (n ^ (n >> 13)) * 1274126177;
            return (byte)(n & 255);
        }
    }

    static byte ToByte(float v) => (byte)MathHelper.Clamp(v * 255f, 0f, 255f);
}
