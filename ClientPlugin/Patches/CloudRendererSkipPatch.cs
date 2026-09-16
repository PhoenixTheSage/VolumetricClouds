using System.Reflection;
using ClientPlugin.Anomaly;
using ClientPlugin.Clouds;
using HarmonyLib;

namespace ClientPlugin.Patches;

/// <summary>
/// Skips Keen textured CloudSphere draws while this pack is registered and replacing.
/// Does not patch MyAtmosphereRenderer.
/// </summary>
[HarmonyPatch]
static class CloudRendererSkipPatch
{
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("VRageRender.MyCloudRenderer");
        if (type == null)
            return null;
        var rc = AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext");
        return rc == null
            ? AccessTools.Method(type, "Render")
            : AccessTools.Method(type, "Render", new[] { rc, typeof(uint) });
    }

    static bool Prefix()
    {
        return !(AnomalyBridge.IsRegistered && CloudRenderer.ShouldHideVanilla);
    }
}
