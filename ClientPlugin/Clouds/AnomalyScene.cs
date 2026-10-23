using System;
using System.Reflection;
using ClientPlugin.Anomaly;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Clouds;

/// <summary>
/// Camera and sun from Anomaly <c>ClientPlugin.Backend.SceneView</c>.
/// </summary>
static class AnomalyScene
{
    public static Vector3D Camera()
    {
        return new Vector3D(Read("CameraX"), Read("CameraY"), Read("CameraZ"));
    }

    public static Vector3 SunLightDirection()
    {
        return new Vector3(ReadF("SunX"), ReadF("SunY"), ReadF("SunZ"));
    }

    static PropertyInfo renderClock;
    static int clockAssemblies = -1;

    public static double FrameSeconds()
    {
        // Anomaly's RenderClock is the draw time on both hosts (DX11 MyCommon.FrameTime).
        var assemblies = renderClock == null ? AppDomain.CurrentDomain.GetAssemblies() : null;
        if (assemblies != null && assemblies.Length != clockAssemblies)
        {
            clockAssemblies = assemblies.Length;
            foreach (var assembly in assemblies)
            {
                Type clock = null;
                try { clock = assembly.GetType("ClientPlugin.Shaders.RenderClock", false); }
                catch { }
                renderClock = clock?.GetProperty("FrameSeconds", BindingFlags.Public | BindingFlags.Static);
                if (renderClock != null)
                    break;
            }
        }
        if (renderClock != null)
            return Convert.ToDouble(renderClock.GetValue(null));
        // Older Anomaly: keep animation static on the guest route.
        if (!AnomalyRenderer.AllowDx11Device)
            return 0d;
        var type = Type.GetType("VRageRender.MyCommon, VRage.Render11");
        var frame = type?.GetProperty("FrameTime", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var seconds = frame?.GetType().GetProperty("Seconds")?.GetValue(frame);
        return seconds == null ? 0d : Convert.ToDouble(seconds);
    }

    public static void DebugLine(Vector3D a, Vector3D b, Color color)
    {
        // VRage.Render's message proxy: DX11 and SE-Vulkan (DebugDrawLine3D replay) both draw it.
        MyRenderProxy.DebugDrawLine3D(a, b, color, color, false, false);
    }

    public static bool TryResolution(out int width, out int height)
    {
        width = (int)Read("Width");
        height = (int)Read("Height");
        return width > 0 && height > 0;
    }

    static double Read(string name)
    {
        var value = Property(name);
        return value == null ? 0d : Convert.ToDouble(value);
    }

    static float ReadF(string name)
    {
        var value = Property(name);
        return value == null ? 0f : Convert.ToSingle(value);
    }

    static object Property(string name)
    {
        var type = Find("ClientPlugin.Backend.SceneView");
        return type?.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    static Type Find(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(fullName, false); }
            catch { continue; }
            if (type != null)
                return type;
        }

        return null;
    }
}
