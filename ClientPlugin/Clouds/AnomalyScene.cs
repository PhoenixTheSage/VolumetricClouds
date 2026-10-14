using System;
using System.Reflection;
using VRageMath;

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

    public static double FrameSeconds()
    {
        var type = Type.GetType("VRageRender.MyCommon, VRage.Render11");
        var frame = type?.GetProperty("FrameTime", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var seconds = frame?.GetType().GetProperty("Seconds")?.GetValue(frame);
        return seconds == null ? 0d : Convert.ToDouble(seconds);
    }

    public static void DebugLine(Vector3D a, Vector3D b, Color color)
    {
        var type = Type.GetType("VRageRender.MyRenderProxy, VRage.Render11");
        var method = type?.GetMethod("DebugDrawLine3D", BindingFlags.Public | BindingFlags.Static);
        method?.Invoke(null, new object[] { a, b, color, color, false });
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
