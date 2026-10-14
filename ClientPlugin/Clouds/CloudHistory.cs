using System;
using System.Reflection;

namespace ClientPlugin.Clouds;

/// <summary>
/// RGBA16F history for the studio and planet resolves. Created through
/// Anomaly's catalog so this assembly does not touch a DX11 device.
/// Alpha -1 tells the resolve to take the raw sample.
/// </summary>
static class CloudHistory
{
    internal const string StudioName = "clouds.studioHistory";
    internal const string VolumeName = "clouds.volumeHistory";

    static int width;
    static int height;
    static int studioKey = int.MinValue;
    static int volumeKey = int.MinValue;
    static bool created;

    public static void Ensure(bool studioLive, int fieldKey)
    {
        AnomalyScene.TryResolution(out int w, out int h);
        w = Math.Max(w, 1);
        h = Math.Max(h, 1);
        if (!created || w != width || h != height)
        {
            EnsureTarget(StudioName, w, h);
            EnsureTarget(VolumeName, w, h);
            width = w;
            height = h;
            created = true;
            studioKey = int.MinValue;
            volumeKey = int.MinValue;
        }

        if (studioLive)
        {
            if (fieldKey != studioKey)
            {
                ClearTarget(StudioName);
                studioKey = fieldKey;
            }
        }
        else if (fieldKey != volumeKey)
        {
            ClearTarget(VolumeName);
            volumeKey = fieldKey;
        }
    }

    public static void Release()
    {
        created = false;
        width = 0;
        height = 0;
        studioKey = int.MinValue;
        volumeKey = int.MinValue;
    }

    static void EnsureTarget(string name, int w, int h)
    {
        var type = Find("ClientPlugin.Buffers.CatalogImages");
        type?.GetMethod("EnsureColorTarget", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, new object[] { "clouds", name, w, h, 1 });
    }

    static void ClearTarget(string name)
    {
        var type = Find("ClientPlugin.Buffers.CatalogImages");
        type?.GetMethod("ClearColorTarget", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, new object[] { name, 0f, 0f, 0f, -1f });
    }

    static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(name, false); }
            catch { continue; }
            if (type != null)
                return type;
        }

        return null;
    }
}
