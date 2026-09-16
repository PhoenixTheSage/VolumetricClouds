using System;
using System.Reflection;
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

    public static bool TryInstall()
    {
        lock (Gate)
        {
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

    static void Populate(object page)
    {
        var t = page.GetType();
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
        Invoke(t, page, "Slider", "Thickness", 0.01f, 0.12f,
            (Func<float>)(() => Config.Current.Thickness),
            (Action<float>)(v => Set(() => Config.Current.Thickness = v)),
            "Shell thickness as a fraction of planet radius", 0.005f);
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
