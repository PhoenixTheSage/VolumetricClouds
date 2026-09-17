using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Anomaly;
using ClientPlugin.Clouds;
using ClientPlugin.RichHud;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "Clouds";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;
    private bool updateFailed;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();
        AnomalyTerminalHook.TryInstall();

        try
        {
            var harmony = new Harmony(Name);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{Name}: Harmony patch failed, Keen clouds stay: {e.Message}");
        }
    }

    public void Dispose()
    {
        ConfigStorage.FlushPending(true);
        CloudRenderer.Publish(null);
        CloudSampler.OnSessionUnloading();
        Instance = null;
    }

    public void Update()
    {
        AnomalyTerminalHook.TryInstall();
        ConfigStorage.FlushPending();
        if (updateFailed)
            return;
        try
        {
            CloudSampler.Update();
            AnomalyTerminalHook.TryRefresh();
        }
        catch (Exception e)
        {
            updateFailed = true;
            CloudRenderer.Publish(null);
            MyLog.Default.Error($"{Name}: Update failed, disabling for this session: {e}");
        }
    }

    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(IReadOnlyDictionary<string, string> assets)
    {
        if (assets != null && assets.TryGetValue("AnomalyPack", out var root))
            AnomalyBridge.TryRegisterPack(root);
        else
            MyLog.Default.Warning($"{Name}: AnomalyPack asset missing");
        AnomalyTerminalHook.TryInstall();
    }

    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(string folder)
    {
        // Named AnomalyPack is registered from the dictionary overload.
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }
}
