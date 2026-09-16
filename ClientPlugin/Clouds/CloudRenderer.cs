using System;
using ClientPlugin.Anomaly;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Clouds;

/// <summary>
/// Render-thread uniforms for the AfterAtmosphere IsolatedMix program.
/// Anomaly owns the draw.
/// </summary>
public static class CloudRenderer
{
    static volatile CloudSnapshot snapshot;

    public static void Publish(CloudSnapshot value)
    {
        snapshot = value;
    }

    /// <summary>
    /// Hide Keen spheres only while a planet snapshot is live. Far-orbit
    /// with a null snapshot must not leave an empty sky.
    /// </summary>
    public static bool ShouldHideVanilla =>
        snapshot != null && Config.Current.Enabled && Config.Current.ReplaceVanilla;

    public static void PushUniforms()
    {
        var snap = snapshot;
        var config = Config.Current;
        if (snap == null || !config.Enabled)
        {
            AnomalyBridge.SetPassEnabled(false);
            AnomalyBridge.SetUniforms(new float[64]);
            return;
        }

        float fade = ComputeDistanceFade(snap);
        if (fade <= 0f)
        {
            AnomalyBridge.SetPassEnabled(false);
            AnomalyBridge.SetUniforms(new float[64]);
            return;
        }

        CloudTextures.EnsureCreated(snap.PlanetSeed);
        AnomalyBridge.PublishTextures();
        AnomalyBridge.SetPassEnabled(true);
        AnomalyBridge.SetUniforms(PackConstants(snap, config, fade));
    }

    static float ComputeDistanceFade(CloudSnapshot snap)
    {
        float distance = (float)(MyRender11.Environment.Matrices.CameraPosition - snap.PlanetCenter).Length();
        if (distance <= snap.FadeStartDistance)
            return 1f;
        return MathHelper.Clamp(
            (snap.FadeEndDistance - distance) / Math.Max(snap.FadeEndDistance - snap.FadeStartDistance, 1f),
            0f, 1f);
    }

    static float CameraToShell(CloudSnapshot snap, Vector3 centerRel)
    {
        float camR = centerRel.Length();
        if (camR > snap.OuterRadius)
            return camR - snap.OuterRadius;
        if (camR < snap.InnerRadius)
            return snap.InnerRadius - camR;
        return 0f;
    }

    static float[] PackConstants(CloudSnapshot snap, Config config, float fade)
    {
        var centerRel = (Vector3)(snap.PlanetCenter - MyRender11.Environment.Matrices.CameraPosition);
        var light = MyRender11.Environment.Data.EnvironmentLight;
        var sunToward = -light.SunLightDirection;
        var planetTint = config.AlbedoTint.ToVector3() * snap.Albedo;
        var tint = Vector3.Lerp(new Vector3(0.93f, 0.95f, 0.98f), planetTint, 0.22f);
        float hdrLift = AnomalyBridge.HasDisplayTenant
            ? MathHelper.Clamp(config.HdrLift, 1f, 16f)
            : 1f;

        float t = (float)MyCommon.FrameTime.Seconds;
        float speed = Math.Max(config.WindSpeed, 0f);
        float spin = Math.Max(snap.AngularVelocity, 0f);
        // Time-based UV drift (visible in seconds) plus planet CloudLayer spin.
        float windU = t * (speed * 0.00115f + spin * 0.018f);
        float windV = t * (speed * 0.00041f + spin * 0.006f);
        float windW = t * speed * 0.00062f;
        // Puff scale stays ~0.5–0.9 km so a taller cirrus shell does not smear into fog.
        float volumeSize = MathHelper.Clamp(Math.Max(snap.InnerRadius * 0.018f, 560f), 560f, 880f);
        float camToShell = CameraToShell(snap, centerRel);

        return new[]
        {
            centerRel.X, centerRel.Y, centerRel.Z, snap.InnerRadius,
            sunToward.X, sunToward.Y, sunToward.Z, snap.OuterRadius,
            tint.X, tint.Y, tint.Z, MathHelper.Clamp(config.Coverage, 0.05f, 2f),
            windU, windV, windW, MathHelper.Clamp(config.Density, 0.05f, 1.2f),
            Math.Min(config.StepCount, Config.MaxRaymarchSteps), fade,
            MathHelper.Clamp(config.CirrusStrength, 0f, 1.5f), config.LightStepCount,
            volumeSize, hdrLift, snap.MaxHillRadius, snap.AtmosphereRadius,
            snap.PlanetUp.X, snap.PlanetUp.Y, snap.PlanetUp.Z, camToShell,
            0f, 0f, 0f, 0f,
            0.25f, 0.25f, 0.30f, 1.0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
        };
    }
}
