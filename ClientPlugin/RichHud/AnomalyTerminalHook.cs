using System;
using System.Reflection;
using ClientPlugin.Clouds;
using ClientPlugin.Settings;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.RichHud;

internal static class AnomalyTerminalHook
{
    public const string RegistryTypeName = "ClientPlugin.RichHud.TerminalConfigRegistry";
    public const string FolderTitle = "Volumetric Clouds";
    public const string SettingsPage = "Settings";

    static readonly object Gate = new();
    static bool installed;
    static bool overlayInstalled;
    static object pageInstance;
    static Type pageType;
    static string lastHud;

    public static bool TryInstall()
    {
        lock (Gate)
        {
            TryInstallOverlay();
            if (installed)
                return true;

            var page = RequestPage();
            if (page == null)
                return false;

            Populate(page);
            installed = true;
            MyLog.Default.WriteLine($"{Plugin.Name}: Rich HUD page under Anomaly Shaders / {FolderTitle} / {SettingsPage}");
            return true;
        }
    }

    public static void TryRefresh()
    {
        object page;
        Type t;
        lock (Gate)
        {
            page = pageInstance;
            t = pageType;
        }

        if (page == null || t == null)
            return;

        var line = CloudSampler.HudLine ?? "No planet in range yet";
        if (line == lastHud)
            return;
        lastHud = line;
        Invoke(t, page, "Refresh");
    }

    static object RequestPage()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == null || assembly == typeof(AnomalyTerminalHook).Assembly)
                continue;

            Type registry;
            try
            {
                registry = assembly.GetType(RegistryTypeName, false, false);
            }
            catch
            {
                continue;
            }

            if (registry == null)
                continue;

            var requestFolder = registry.GetMethod("RequestFolderPage", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(string) }, null);
            try
            {
                return requestFolder?.Invoke(null, new object[] { FolderTitle, SettingsPage });
            }
            catch (Exception e)
            {
                MyLog.Default.Warning($"{Plugin.Name}: RequestFolderPage failed: {e.Message}");
                return null;
            }
        }

        return null;
    }

    static void TryInstallOverlay()
    {
        if (overlayInstalled)
            return;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == null || assembly == typeof(AnomalyTerminalHook).Assembly)
                continue;

            Type registry;
            try
            {
                registry = assembly.GetType("ClientPlugin.RichHud.HudOverlayRegistry", false, false);
            }
            catch
            {
                continue;
            }

            if (registry == null)
                continue;

            var register = registry.GetMethod("Register", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(Func<string>) }, null);
            if (register == null)
                continue;

            try
            {
                register.Invoke(null, new object[]
                {
                    "volumetric.clouds",
                    (Func<string>)(() => CloudSampler.HudLine)
                });
                overlayInstalled = true;
                return;
            }
            catch (Exception e)
            {
                MyLog.Default.Warning($"{Plugin.Name}: HudOverlay Register failed: {e.Message}");
                return;
            }
        }
    }

    static void Populate(object page)
    {
        var t = page.GetType();
        pageInstance = page;
        pageType = t;
        lastHud = null;
        Invoke(t, page, "Category", "Volumetric Clouds");
        Invoke(t, page, "Checkbox", "Enabled",
            (Func<bool>)(() => Config.Current.Enabled),
            (Action<bool>)(v => Set(() => Config.Current.Enabled = v)),
            "Master switch for raymarched clouds");
        Invoke(t, page, "Checkbox", "Replace vanilla layers",
            (Func<bool>)(() => Config.Current.ReplaceVanilla),
            (Action<bool>)(v => Set(() => Config.Current.ReplaceVanilla = v)),
            "Hide Keen CloudSphere layers while this pack is drawing");
        Invoke(t, page, "Dropdown", "Quality", typeof(CloudQuality),
            (Func<object>)(() => Config.Current.Quality),
            (Action<object>)(v => Set(() => Config.Current.Quality = (CloudQuality)v)),
            "Raymarching quality (samples per pixel)");
        Invoke(t, page, "Slider", "Coverage", 0.05f, 2f,
            (Func<float>)(() => Config.Current.Coverage),
            (Action<float>)(v => Set(() => Config.Current.Coverage = v)),
            "Weather-map coverage scale", 0.01f);
        Invoke(t, page, "Slider", "Density", 0.05f, 1.2f,
            (Func<float>)(() => Config.Current.Density),
            (Action<float>)(v => Set(() => Config.Current.Density = v)),
            "Cloud optical density", 0.01f);

        Invoke(t, page, "Category", "Height",
            "0 = terrain, 1 = visual air top. Live km is the Clouds overlay when Master is in the world.");
        Invoke(t, page, "Label", "Column",
            (Func<string>)(() => CloudSampler.HudLine ?? "No planet in range yet"));
        Invoke(t, page, "Slider", "Min height", 0f, 1f,
            (Func<float>)(() => Config.Current.BaseAltitude),
            (Action<float>)(v => Set(() => Config.Current.BaseAltitude = v)),
            "Bottom of the cloud column. 0 = terrain, 1 = visual atmosphere edge", 0.01f);
        Invoke(t, page, "Slider", "Max height", 0f, 1f,
            (Func<float>)(() => Config.Current.MaxAltitude),
            (Action<float>)(v => Set(() => Config.Current.MaxAltitude = v)),
            "Top of the cloud column. 0 = terrain, 1 = visual atmosphere edge (air top). Raise to 1 so the deck clears hill tops.", 0.01f);

        Invoke(t, page, "Category", "Appearance");
        Invoke(t, page, "Slider", "Wind speed", 0f, 8f,
            (Func<float>)(() => Config.Current.WindSpeed),
            (Action<float>)(v => Set(() => Config.Current.WindSpeed = v)),
            "Cloud drift. 1 wraps the weather map in about 15 minutes", 0.05f);
        Invoke(t, page, "Slider", "Cirrus", 0f, 1.5f,
            (Func<float>)(() => Config.Current.CirrusStrength),
            (Action<float>)(v => Set(() => Config.Current.CirrusStrength = v)),
            "High-altitude ice veil in the upper shell", 0.01f);
        Invoke(t, page, "Color", "Albedo tint",
            (Func<Color>)(() => Config.Current.AlbedoTint),
            (Action<Color>)(v => Set(() => Config.Current.AlbedoTint = v)),
            "Multiplies the planet CloudLayer albedo");
        Invoke(t, page, "Slider", "HDR lift", 1f, 16f,
            (Func<float>)(() => Config.Current.HdrLift),
            (Action<float>)(v => Set(() => Config.Current.HdrLift = v)),
            "Extra brightness when an HDR Display pack is live. 1 = same as SDR.", 0.5f);

        Invoke(t, page, "Category", "Distance fade");
        Invoke(t, page, "Slider", "Fade start", 1f, 40f,
            (Func<float>)(() => Config.Current.FadeStartFactor),
            (Action<float>)(v => Set(() => Config.Current.FadeStartFactor = v)),
            "Distance where clouds start fading (atmosphere radii)", 0.1f);
        Invoke(t, page, "Slider", "Fade end", 1f, 40f,
            (Func<float>)(() => Config.Current.FadeEndFactor),
            (Action<float>)(v => Set(() => Config.Current.FadeEndFactor = v)),
            "Distance where clouds vanish (atmosphere radii)", 0.1f);
    }

    static void Set(Action apply)
    {
        apply();
        ConfigStorage.Save(Config.Current);
    }

    static void Invoke(Type type, object instance, string name, params object[] args)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var method in type.GetMethods(flags))
        {
            if (method.Name != name || method.IsGenericMethodDefinition)
                continue;
            var parameters = method.GetParameters();
            if (args.Length > parameters.Length)
                continue;
            if (args.Length < parameters.Length)
            {
                var optional = true;
                for (var j = args.Length; j < parameters.Length; j++)
                {
                    if (!parameters[j].IsOptional && !parameters[j].HasDefaultValue)
                    {
                        optional = false;
                        break;
                    }
                }

                if (!optional)
                    continue;
            }

            var match = true;
            for (var i = 0; i < args.Length; i++)
            {
                var value = args[i];
                var expected = parameters[i].ParameterType;
                if (value == null)
                {
                    if (expected.IsValueType && Nullable.GetUnderlyingType(expected) == null)
                    {
                        match = false;
                        break;
                    }

                    continue;
                }

                if (!expected.IsInstanceOfType(value))
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;
            method.Invoke(instance, PadDefaults(method, args));
            return;
        }

        MyLog.Default.Warning($"{Plugin.Name}: Anomaly terminal missing {name}");
    }

    static object[] PadDefaults(MethodInfo method, object[] args)
    {
        var parameters = method.GetParameters();
        if (args.Length == parameters.Length)
            return args;
        var padded = new object[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            padded[i] = i < args.Length ? args[i] : parameters[i].DefaultValue;
        return padded;
    }
}
