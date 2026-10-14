using ClientPlugin.Clouds;
using HarmonyLib;
using Sandbox.Game.World;

namespace ClientPlugin.Patches;

/// <summary>
/// CPU weather must be pending before the load screen ends. AfterLoading is
/// ambiguous on IMySession vs MySession, so patch the raise instead.
/// </summary>
[HarmonyPatch(typeof(MySession), "RaiseAfterLoading")]
static class SessionWeatherPreparePatch
{
    static void Postfix()
    {
        CloudSampler.PrepareSessionWeather();
    }
}
