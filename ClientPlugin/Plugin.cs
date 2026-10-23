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
[assembly: AssemblyVersion("1.2.2.0")]
[assembly: AssemblyFileVersion("1.2.2.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "Clouds";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;
    private int climateFaults;
    private bool deviceRouted;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();
        AnomalyTerminalHook.TryInstall();
        ApplyDeviceRoute();
    }

    void ApplyDeviceRoute()
    {
        if (deviceRouted)
            return;
        if (!AnomalyRenderer.Found || !AnomalyRenderer.Decided)
            return;
        deviceRouted = true;
        if (!AnomalyRenderer.AllowDx11Device)
        {
            if (AnomalyRenderer.AllowGuestWork)
            {
                try
                {
                    new Harmony(Name).CreateClassProcessor(typeof(Patches.SessionWeatherPreparePatch)).Patch();
                }
                catch (Exception e)
                {
                    MyLog.Default.Warning($"{Name}: session weather patch skipped: {e.Message}");
                }
            }
            else
            {
                MyLog.Default.WriteLine(AnomalyRenderer.LinuxRequiresGuest
                    ? $"{Name}: Linux stays idle until Anomaly's Vulkan guest bus is open"
                    : $"{Name}: Keen cloud skip stays off ({AnomalyRenderer.ActiveName})");
            }
            return;
        }

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
        CloudHistory.Release();
        SharedMediumAdapter.Dispose();
        CloudSampler.OnSessionUnloading();
        Instance = null;
    }

    public void Update()
    {
        PluginStatusPage.TryInstall("Volumetric Clouds", Config.ShowStatus, RichHud.AnomalyTerminalHook.PopulateStatus);
        AnomalyRenderer.Observe();
        ApplyDeviceRoute();
        AnomalyTerminalHook.TryInstall();
        ConfigStorage.FlushPending();
        if (AnomalyRenderer.LinuxRequiresGuest)
            return;
        StudioPlacer.Update();
        try
        {
            CloudSampler.Update();
            if (AnomalyRenderer.AllowGuestWork)
                CloudRenderer.PushUniforms();
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Name}: planet sample failed: {e.Message}");
        }

        try
        {
            CloudTextures.StepClimate();
            climateFaults = 0;
        }
        catch (Exception e)
        {
            // One bad climate step must not drop the snapshot. That turned
            // the pack off and brought Keen's cloud sphere back for the session.
            climateFaults++;
            if (climateFaults <= 3 || climateFaults % 240 == 0)
                MyLog.Default.Error($"{Name}: climate step failed: {e.Message}");
        }

        try
        {
            AnomalyTerminalHook.TryRefresh();
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{Name}: terminal refresh skipped: {e.Message}");
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
