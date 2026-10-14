using System;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Settings;

public static class ConfigStorage
{
    const int DebounceMs = 400;

    static readonly string ConfigFileName = string.Concat(Plugin.Name, ".cfg");
    static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Config));
    static readonly object Gate = new();
    static readonly object WriteGate = new();
    static Config pending;
    static bool dirty;
    static int lastRequestTick;
    static int writesInFlight;

    static string ConfigFilePath => Path.Combine(MyFileSystem.UserDataPath, "Storage", ConfigFileName);

    /// <summary>
    /// Marks the config dirty. Rich HUD sliders fire this while dragging.
    /// <see cref="FlushPending"/> waits ~400 ms of quiet, then writes on a
    /// worker so Plugin.Update never hits File.CreateText.
    /// </summary>
    public static void Save(Config config)
    {
        if (config == null)
            return;
        lock (Gate)
        {
            pending = config;
            dirty = true;
            lastRequestTick = Environment.TickCount;
        }
    }

    public static void FlushPending(bool force = false)
    {
        Config toWrite = null;
        lock (Gate)
        {
            if (dirty && pending != null)
            {
                if (!force && unchecked(Environment.TickCount - lastRequestTick) < DebounceMs)
                    return;
                toWrite = pending;
                dirty = false;
            }
            else if (!force)
            {
                return;
            }
        }

        if (!force)
        {
            Interlocked.Increment(ref writesInFlight);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Write(toWrite); }
                finally { Interlocked.Decrement(ref writesInFlight); }
            });
            return;
        }

        var spins = 0;
        while (Volatile.Read(ref writesInFlight) > 0 && spins++ < 2000)
            Thread.Sleep(1);
        if (toWrite != null)
            Write(toWrite);
    }

    static void Write(Config config)
    {
        var path = ConfigFilePath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            lock (WriteGate)
            {
                using (var text = File.CreateText(path))
                    Serializer.Serialize(text, config);
            }
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{ConfigFileName}: Failed to save config file: {e.Message}");
        }
    }

    public static Config Load()
    {
        var path = ConfigFilePath;
        if (!File.Exists(path))
            return Config.Default;

        try
        {
            var xml = File.ReadAllText(path);
            xml = xml.Replace("<DebugView>OctreeVsWeather</DebugView>", "<DebugView>HeightProfile</DebugView>");
            xml = xml.Replace("<DebugView>Octree</DebugView>", "<DebugView>HeightProfile</DebugView>");
            using (var streamReader = new StringReader(xml))
            {
                var loaded = (Config)Serializer.Deserialize(streamReader) ?? Config.Default;
                MigrateHeight(loaded);
                return loaded;
            }
        }
        catch (Exception)
        {
            MyLog.Default.Warning($"{ConfigFileName}: Failed to read config file: {ConfigFilePath}");
        }

        return Config.Default;
    }

    /// <summary>
    /// 0.55 was a short-lived default that buried Pertam's entire deck under
    /// MaximumRadius (~30.8 km). Do not rewrite Min 0.15–0.45 — that is a
    /// legal sea-relative floor (Earthlike default is 0.23).
    /// </summary>
    static void MigrateHeight(Config config)
    {
        if (config == null)
            return;
        if (Math.Abs(config.MaxAltitude - 0.55f) < 0.0005f)
            config.MaxAltitude = 0.75f;
        // Old pair was camera-to-center × atmosphere (7–15). That is not
        // view range. Keep the new default unless the file already stored
        // a short HorizonRange-scale end.
        if (config.FadeEndFactor > 0.4f && config.FadeEndFactor <= 8f &&
            Math.Abs(config.HorizonRange - 2.5f) < 0.0005f)
            config.HorizonRange = Math.Max(0.5f, Math.Min(8f, config.FadeEndFactor));
        config.TypeThickness = config.TypeThickness;
        config.TypeShape = config.TypeShape;
    }
}
