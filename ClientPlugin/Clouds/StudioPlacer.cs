using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Engine.Physics;
using Sandbox.Game.World;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Clouds;

/// <summary>
/// Drops the Volume Studio box into the live world. The ghost follows the
/// crosshair until left click. Page Up/Down, Home/End, and Insert/Delete
/// turn it the way a block does. Right click or Escape leaves placement.
/// </summary>
public static class StudioPlacer
{
    public const int MaxVolumes = 8;
    public const int FloatsPerVolume = 16;
    const float RayMetres = 20000f;
    const float MinHitMetres = 2f;

    struct Volume
    {
        public Vector3D Position;
        public MatrixD Basis;
        public Vector3 Half;
    }

    static readonly object Gate = new();
    static readonly List<Volume> Placed = new();
    static readonly List<MyPhysics.HitInfo> Hits = new();
    static bool placing;
    static bool basisReady;
    static int ignoreClicks;
    static MatrixD basis = MatrixD.Identity;
    static Vector3D ghost;
    static bool ghostValid;
    static StudioVolumeBuffer gpu;

    public static int PlacedCount
    {
        get { lock (Gate) return Placed.Count; }
    }

    public static bool IsPlacing
    {
        get { lock (Gate) return placing; }
    }

    public static void Begin()
    {
        lock (Gate)
        {
            placing = true;
            basisReady = false;
            ignoreClicks = 8;
            ghostValid = false;
        }

        CloseTerminal();
    }

    public static void RemoveAll()
    {
        lock (Gate)
            Placed.Clear();
    }

    public static void Update()
    {
        bool active;
        lock (Gate)
            active = placing;
        if (!active)
            return;

        var camera = MySector.MainCamera;
        if (camera == null)
            return;

        lock (Gate)
        {
            if (!basisReady)
            {
                basis = Facing(camera.WorldMatrix);
                basisReady = true;
            }

            Turn(camera.WorldMatrix);
            ghostValid = Aim(camera.WorldMatrix, camera.Position, out ghost);
            if (ignoreClicks > 0)
                ignoreClicks--;
            else if (ghostValid && MyInput.Static.IsNewLeftMousePressed())
                Commit();
            if (MyInput.Static.IsNewRightMousePressed() || MyInput.Static.IsNewKeyPressed(MyKeys.Escape))
                placing = false;
        }

        if (ghostValid)
            DrawBox(ghost, basis, HalfNow(), new Color(80, 220, 255));
    }

    /// <summary>Render thread. Camera-relative records, ghost last.</summary>
    public static ICloudSrv Upload(Vector3D camera)
    {
        var data = new float[MaxVolumes * FloatsPerVolume];
        int count;
        lock (Gate)
        {
            count = 0;
            for (var i = 0; i < Placed.Count && count < MaxVolumes; i++)
                Write(data, ref count, Placed[i].Position, Placed[i].Basis, Placed[i].Half, camera);
            if (placing && ghostValid && count < MaxVolumes)
                Write(data, ref count, ghost, basis, HalfNow(), camera);
        }

        if (count == 0)
            return gpu;

        try
        {
            gpu ??= new StudioVolumeBuffer("CloudsStudioVolumes");
            gpu.Update(data);
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Plugin.Name}: studio volume upload failed: {e.Message}");
        }

        return gpu;
    }

    public static bool HasWork
    {
        get
        {
            lock (Gate)
                return Placed.Count > 0 || (placing && ghostValid);
        }
    }

    static void Commit()
    {
        if (Placed.Count >= MaxVolumes)
            return;
        Placed.Add(new Volume
        {
            Position = ghost,
            Basis = basis,
            Half = HalfNow(),
        });
    }

    static Vector3 HalfNow()
    {
        var c = Config.Current;
        return new Vector3(
            Math.Max(c.StudioWidth, 1f),
            Math.Max(c.StudioHeight, 1f),
            Math.Max(c.StudioDepth, 1f));
    }

    static MatrixD Facing(MatrixD camera)
    {
        var forward = camera.Forward;
        forward.Y = 0;
        if (forward.LengthSquared() < 1e-6)
            forward = camera.Forward;
        forward.Normalize();
        return MatrixD.CreateWorld(Vector3D.Zero, forward, Vector3D.Up);
    }

    static void Turn(MatrixD camera)
    {
        var input = MyInput.Static;
        if (input.IsNewKeyPressed(MyKeys.PageUp))
            Yaw(Vector3D.Up, 1);
        if (input.IsNewKeyPressed(MyKeys.PageDown))
            Yaw(Vector3D.Up, -1);
        if (input.IsNewKeyPressed(MyKeys.Home))
            Yaw(camera.Right, 1);
        if (input.IsNewKeyPressed(MyKeys.End))
            Yaw(camera.Right, -1);
        if (input.IsNewKeyPressed(MyKeys.Insert))
            Yaw(camera.Forward, 1);
        if (input.IsNewKeyPressed(MyKeys.Delete))
            Yaw(camera.Forward, -1);
    }

    static void Yaw(Vector3D axis, int sign)
    {
        if (axis.LengthSquared() < 1e-8)
            return;
        axis.Normalize();
        var spin = MatrixD.CreateFromAxisAngle(axis, sign * MathHelper.PiOver2);
        basis = MatrixD.Multiply(spin, basis);
        Orthonormal();
    }

    static void Orthonormal()
    {
        var forward = Vector3D.Normalize(basis.Forward);
        var up = basis.Up - forward * Vector3D.Dot(basis.Up, forward);
        if (up.LengthSquared() < 1e-8)
            up = Math.Abs(forward.Y) < 0.9 ? Vector3D.Up : Vector3D.Right;
        up.Normalize();
        var right = Vector3D.Normalize(Vector3D.Cross(forward, up));
        up = Vector3D.Cross(right, forward);
        basis = MatrixD.Identity;
        basis.Right = right;
        basis.Up = up;
        basis.Forward = forward;
    }

    static bool Aim(MatrixD camera, Vector3D eye, out Vector3D point)
    {
        point = eye + camera.Forward * Math.Max(Config.Current.StudioDistance, MinHitMetres);
        var end = eye + camera.Forward * RayMetres;
        Hits.Clear();
        try
        {
            MyPhysics.CastRay(eye, end, Hits);
        }
        catch
        {
            return true;
        }

        var best = double.MaxValue;
        var hit = false;
        for (var i = 0; i < Hits.Count; i++)
        {
            var distance = (Hits[i].Position - eye).Length();
            if (distance < MinHitMetres || distance >= best)
                continue;
            best = distance;
            point = Hits[i].Position;
            hit = true;
        }

        return hit || true;
    }

    static void Write(float[] data, ref int count, Vector3D position, MatrixD box, Vector3 half, Vector3D camera)
    {
        var o = count * FloatsPerVolume;
        var center = position - camera;
        data[o] = (float)center.X;
        data[o + 1] = (float)center.Y;
        data[o + 2] = (float)center.Z;
        PutAxis(data, o + 4, box.Right, half.X);
        PutAxis(data, o + 8, box.Up, half.Y);
        PutAxis(data, o + 12, box.Forward, half.Z);
        count++;
    }

    static void PutAxis(float[] data, int o, Vector3D axis, float half)
    {
        data[o] = (float)axis.X;
        data[o + 1] = (float)axis.Y;
        data[o + 2] = (float)axis.Z;
        data[o + 3] = half;
    }

    static void DrawBox(Vector3D center, MatrixD box, Vector3 half, Color color)
    {
        var x = box.Right * half.X;
        var y = box.Up * half.Y;
        var z = box.Forward * half.Z;
        var c = new[]
        {
            center - x - y - z, center + x - y - z, center + x + y - z, center - x + y - z,
            center - x - y + z, center + x - y + z, center + x + y + z, center - x + y + z,
        };
        void Edge(int a, int b) => AnomalyScene.DebugLine(c[a], c[b], color);
        Edge(0, 1); Edge(1, 2); Edge(2, 3); Edge(3, 0);
        Edge(4, 5); Edge(5, 6); Edge(6, 7); Edge(7, 4);
        Edge(0, 4); Edge(1, 5); Edge(2, 6); Edge(3, 7);
    }

    static void CloseTerminal()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == null)
                continue;
            Type terminal;
            try
            {
                terminal = assembly.GetType("RichHudFramework.UI.Client.RichHudTerminal", false);
            }
            catch
            {
                continue;
            }

            var close = terminal?.GetMethod("CloseMenu", BindingFlags.Public | BindingFlags.Static);
            if (close == null)
                continue;
            try
            {
                close.Invoke(null, null);
            }
            catch (Exception e)
            {
                MyLog.Default.Warning($"{Plugin.Name}: terminal close failed: {e.Message}");
            }

            return;
        }
    }

    sealed class StudioVolumeBuffer : ICloudSrv, IDisposable
    {
        float[] values = Array.Empty<float>();

        public string Name { get; }
        public int Width => values.Length;
        public int Height => 1;
        public int Depth => 1;
        public byte[] Rgba => null;
        public byte[][] Mips => null;
        public float[] Structured => values;
        public int Stride => 16;

        public StudioVolumeBuffer(string name) => Name = name;

        public void Update(float[] data)
        {
            if (data == null)
                return;
            values = (float[])data.Clone();
        }

        public void Dispose() => values = Array.Empty<float>();
    }
}
