using System;
using ClientPlugin.Anomaly;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Clouds;

/// <summary>
/// Render-thread uniforms for the AfterAtmosphere IsolatedMix program.
/// Anomaly owns the draw.
/// </summary>
public static class CloudRenderer
{
    static volatile CloudSnapshot snapshot;
    static CloudPackDiagnostics lastPack = new();
    static Vector3 prevWind;

    /// <summary>Last render-thread pack dump for the status screen.</summary>
    public static CloudPackDiagnostics LastPack => lastPack;

    /// <summary>
    /// Metres past visual air before <see cref="ViewLod"/> reaches 1.
    /// Matches shader <c>CLOUD_VIEW_LOD_FAR</c> / <c>AnomalyVolumeViewLod</c>.
    /// </summary>
    public const float ViewLodFarMeters = 40000f;

    /// <summary>
    /// Stay at view-LOD 0 through extras air top plus this pad so Fit /
    /// packed Outer cannot open CamToShell while still in the blue.
    /// </summary>
    public const float VisualAirPadMeters = 80f;

    public static float ViewLod(float camToShell) =>
        MathHelper.Clamp(camToShell / ViewLodFarMeters, 0f, 1f);

    public static void Publish(CloudSnapshot value)
    {
        snapshot = value;
    }

    /// <summary>
    /// Planetary volumes are off. Hide Keen spheres only while the studio
    /// Replace owns the frame. Studio off leaves Keen's atmosphere and clouds.
    /// </summary>
    public static bool ShouldHideVanilla =>
        Config.Current.Enabled && Config.Current.VolumeStudio;

    /// <summary>
    /// Planetary IsolatedMix, sun shadow, and the shared medium stay off
    /// until the studio cumulus is signed off. Flip this to draw the shell again.
    /// </summary>
    const bool PlanetaryVolumesEnabled = false;

    public static void PushUniforms()
    {
        if (!PlanetaryVolumesEnabled || Config.Current.VolumeStudio)
        {
            PushStudioFrame();
            return;
        }

        AnomalyBridge.SetStudioEnabled(false);
        var snap = snapshot;
        var config = Config.Current;
        if (snap == null || !config.Enabled)
        {
            lastPack = new CloudPackDiagnostics();
            SharedMediumAdapter.Publish(null,null);
            AnomalyBridge.SetPassEnabled(false);
            AnomalyBridge.SetUniforms(new float[64]);
            return;
        }

        float fade = ComputeDistanceFade(snap);
        if (fade <= 0f)
        {
            lastPack = new CloudPackDiagnostics
            {
                Valid = true,
                Fade = 0f,
                CamToShell = CameraToShell(snap,
                    (Vector3)(snap.PlanetCenter - AnomalyScene.Camera())),
                PackedInner = snap.InnerRadius,
                PackedOuter = snap.OuterRadius,
                PackedHills = snap.MaxHillRadius,
                PackedAirTop = snap.VisualCeil > snap.MaxHillRadius + 40f
                    ? snap.VisualCeil
                    : snap.AtmosphereRadius,
                PackedTerrain = snap.TerrainRadius,
            };
            SharedMediumAdapter.Publish(null,null);
            AnomalyBridge.SetPassEnabled(false);
            AnomalyBridge.SetUniforms(new float[64]);
            return;
        }

        CloudHistory.Ensure(false, config.WindSpeed.GetHashCode());
        CloudTextures.EnsureCreated(snap.PlanetSeed, snap.WeatherTexture);
        CloudTextures.UploadLiveWeather();
        CloudTextures.UploadStorms((Vector3)(snap.PlanetCenter - AnomalyScene.Camera()));
        // Keep IsolatedMix up on a stale field while the worker finishes a
        // new seed. Disabling until WeatherReady was approach pop-in.
        if (!CloudTextures.HasWeather)
        {
            lastPack = new CloudPackDiagnostics
            {
                Valid = true,
                Fade = fade,
                CamToShell = CameraToShell(snap,
                    (Vector3)(snap.PlanetCenter - AnomalyScene.Camera())),
                PackedInner = snap.InnerRadius,
                PackedOuter = snap.OuterRadius,
                PackedHills = snap.MaxHillRadius,
                PackedAirTop = snap.VisualCeil > snap.MaxHillRadius + 40f
                    ? snap.VisualCeil
                    : snap.AtmosphereRadius,
                PackedTerrain = snap.TerrainRadius,
            };
            SharedMediumAdapter.Publish(null, null);
            AnomalyBridge.SetPassEnabled(false);
            AnomalyBridge.SetUniforms(new float[64]);
            return;
        }

        AnomalyBridge.PublishTextures();
        AnomalyBridge.SetPassEnabled(true);
        var uniforms = PackConstants(snap, config, fade);
        RecordPack(uniforms, config);
        AnomalyBridge.SetUniforms(uniforms);
        SharedMediumAdapter.Publish(snap, uniforms);
    }

    static void RecordPack(float[] u, Config config)
    {
        if (u == null || u.Length < 44)
        {
            lastPack = new CloudPackDiagnostics();
            return;
        }

        lastPack = new CloudPackDiagnostics
        {
            Valid = true,
            PackedInner = u[3],
            PackedOuter = u[7],
            Coverage = u[11],
            Spacing = config.Spacing,
            Density = u[15],
            Fade = u[17],
            VolumeSize = u[20],
            PackedHills = u[22],
            PackedAirTop = u[23],
            CamToShell = u[27],
            ShapeScale = u[24],
            DetailScale = u[25],
            ErosionStrength = u[26],
            SampleSpacing = u[63],
            StepBudget = u[16],
            LightStepBudget = u[19] - 16f * (int)(u[19] / 16f),
            Low = u[37],
            Mid = u[38],
            High = u[39],
            WeatherScale = u[40],
            PackedTerrain = u[42],
            AtmosphereFit = u[43],
            HasDisplayTenant = AnomalyBridge.HasDisplayTenant,
            DebugView = (int)(u[19] / 16f),
        };
    }

    static float ComputeDistanceFade(CloudSnapshot snap)
    {
        float distance = (float)(AnomalyScene.Camera() - snap.PlanetCenter).Length();
        if (distance <= snap.FadeStartDistance)
            return 1f;
        return MathHelper.Clamp(
            (snap.FadeEndDistance - distance) / Math.Max(snap.FadeEndDistance - snap.FadeStartDistance, 1f),
            0f, 1f);
    }

    internal static float CameraToShell(CloudSnapshot snap, Vector3 centerRel)
    {
        float camR = centerRel.Length();
        float hill = snap.MaxHillRadius > 1f ? snap.MaxHillRadius : snap.InnerRadius;
        float airTop = snap.VisualCeil > hill + 40f
            ? snap.VisualCeil
            : snap.AtmosphereRadius;
        float visual = airTop + VisualAirPadMeters;
        if (camR <= visual)
            return 0f;
        return camR - visual;
    }

    static float[] PackConstants(CloudSnapshot snap, Config config, float fade)
    {
        var centerRel = (Vector3)(snap.PlanetCenter - AnomalyScene.Camera());
        var sunToward = -AnomalyScene.SunLightDirection();
        var planetTint = config.AlbedoTint.ToVector3() * snap.Albedo;
        var tint = Vector3.Lerp(new Vector3(0.93f, 0.95f, 0.98f), planetTint, 0.22f);
        float hdrLift = AnomalyBridge.HasDisplayTenant
            ? MathHelper.Clamp(config.HdrLift, 1f, 16f)
            : 1f;

        float t = (float)AnomalyScene.FrameSeconds();
        float speed = Math.Max(config.WindSpeed, 0f);
        float spin = Math.Max(snap.AngularVelocity, 0f);
        // Time-based UV drift (visible in seconds) plus planet CloudLayer spin.
        float windU = t * (speed * 0.00115f + spin * 0.018f);
        float windV = t * (speed * 0.00041f + spin * 0.006f);
        float windW = t * speed * 0.00062f;
        // Macro weather on the hill shell (stable if Min moves). Micro puffs
        // from the sea→air column, not hill→air.
        float airTop = snap.VisualCeil > snap.MaxHillRadius + 40f
            ? snap.VisualCeil
            : snap.AtmosphereRadius;
        float sea = snap.SeaRadius > 1f ? snap.SeaRadius : snap.TerrainRadius;
        // Equirectangular climate: one wrap of the hill circumference.
        float weatherScale = MathHelper.TwoPi * Math.Max(snap.MaxHillRadius, 1f);
        // HZD / Nubis Evolved: 128³ Worley-4 cells ARE the cloud objects.
        // Cubed inSamplePosition*0.01 (100 m) is ValueErosion uprez — not this.
        // airSpan*0.055 clamped to 640 m forced ~180 m foam inside radial tubes.
        float volumeSize = CloudTextures.EvolvedShapePeriodMetres;
        float camToShell = CameraToShell(snap, centerRel);

        var values = new[]
        {
            centerRel.X, centerRel.Y, centerRel.Z, snap.InnerRadius,
            sunToward.X, sunToward.Y, sunToward.Z, snap.OuterRadius,
            tint.X, tint.Y, tint.Z, MathHelper.Clamp(config.Coverage, 0.05f, 2f),
            windU, windV, windW, MathHelper.Clamp(config.Density, 0.05f, 1.2f),
            Math.Min(config.StepCount, Config.MaxRaymarchSteps), fade,
            MathHelper.Clamp(config.CirrusStrength, 0f, 1.5f),
            config.LightStepCount + 16 * MathHelper.Clamp((int)config.DebugView, 0, 18),
            volumeSize, hdrLift, snap.MaxHillRadius, airTop,
            MathHelper.Clamp(config.ShapeScale, 0.5f, 3f),
            MathHelper.Clamp(config.DetailScale, 0.25f, 4f),
            MathHelper.Clamp(config.ErosionStrength, 0f, 2f),
            camToShell,
            prevWind.X,
            prevWind.Y,
            prevWind.Z,
            config.TypeThicknessAt(0),
            config.TypeThicknessAt(1), config.TypeThicknessAt(2),
            config.TypeThicknessAt(3), config.TypeThicknessAt(4),
            snap.TerrainHug,
            MathHelper.Clamp(config.LowCoverage, 0f, 1.5f),
            0f,
            MathHelper.Clamp(config.CirrusStrength, 0f, 1.5f),
            weatherScale, 0f, snap.TerrainRadius,
            MathHelper.Clamp(config.AtmosphereFit, 0.35f, 1f),
            config.TypeThicknessAt(5), config.TypeThicknessAt(6),
            config.TypeThicknessAt(7), config.TypeThicknessAt(8),
            config.TypeThicknessAt(9),
            config.TypeShapeAt(0), config.TypeShapeAt(1), config.TypeShapeAt(2),
            sea,
            Math.Max(snap.AtmosphereRadius, 1f) * MathHelper.Clamp(config.HorizonRange, 0.5f, 8f),
            0f,
            0f,
            config.TypeShapeAt(3), config.TypeShapeAt(4),
            config.TypeShapeAt(5), config.TypeShapeAt(6),
            config.TypeShapeAt(7), config.TypeShapeAt(8), config.TypeShapeAt(9),
            MathHelper.Clamp(config.SampleSpacing, 20f, 120f),
        };
        if (values.Length != 64)
            MyLog.Default.Error($"{Plugin.Name}: PackConstants {values.Length} floats (need 64)");
        prevWind = new Vector3(windU, windV, windW);
        return values;
    }

    static void PushStudioFrame()
    {
        var config = Config.Current;
        AnomalyBridge.SetPassEnabled(false);
        SharedMediumAdapter.Publish(null, null);
        var camera = AnomalyScene.Camera();
        var volumes = StudioPlacer.Upload(camera);
        var world = config.Enabled && StudioPlacer.HasWork && volumes != null && CloudTextures.EnsureNoise();
        var studio = config.Enabled && config.VolumeStudio && CloudTextures.EnsureNoise();
        if (!studio && !world)
        {
            lastPack = new CloudPackDiagnostics();
            AnomalyBridge.SetStudioEnabled(false);
            AnomalyBridge.SetStudioWorldEnabled(false);
            return;
        }

        AnomalyBridge.PublishTextures();
        if (world)
            AnomalyBridge.PublishStudioVolumes(volumes);
        if (studio)
            CloudHistory.Ensure(true, StudioFieldKey(config));
        var uniforms = PackStudio(config);
        RecordStudio(uniforms);
        AnomalyBridge.SetStudioUniforms(uniforms);
        AnomalyBridge.SetStudioEnabled(studio);
        AnomalyBridge.SetStudioWorldEnabled(world);
    }

    static void RecordStudio(float[] u)
    {
        lastPack = new CloudPackDiagnostics
        {
            Valid = true,
            Fade = 1f,
            Density = u.Length > 7 ? u[7] : 0f,
            VolumeSize = u.Length > 11 ? u[11] : 0f,
            ShapeScale = u.Length > 11 ? u[11] : 0f,
            DetailScale = u.Length > 12 ? u[12] : 0f,
            ErosionStrength = u.Length > 13 ? u[13] : 0f,
            SampleSpacing = u.Length > 14 ? u[14] : 0f,
            LightStepBudget = u.Length > 15 ? u[15] : 0f,
            StepBudget = u.Length > 20 ? u[20] : 0f,
            DebugView = u.Length > 3 ? (int)u[3] : 0,
            HasDisplayTenant = AnomalyBridge.HasDisplayTenant,
        };
    }

    static int StudioFieldKey(Config config)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + config.StudioJitter.GetHashCode();
            h = h * 31 + config.StudioSampleSpacing.GetHashCode();
            h = h * 31 + config.StudioYaw.GetHashCode();
            h = h * 31 + config.StudioPitch.GetHashCode();
            h = h * 31 + config.StudioRoll.GetHashCode();
            h = h * 31 + config.StudioWidth.GetHashCode();
            h = h * 31 + config.StudioHeight.GetHashCode();
            h = h * 31 + config.StudioDepth.GetHashCode();
            h = h * 31 + config.StudioWind.GetHashCode();
            return h;
        }
    }

    static float[] PackStudio(Config config)
    {
        float t = (float)AnomalyScene.FrameSeconds();
        float speed = Math.Max(config.StudioWind, 0f);
        var tint = config.AlbedoTint.ToVector3();
        return new[]
        {
            config.StudioYaw, config.StudioLift, -config.StudioDistance, (float)MathHelper.Clamp((int)config.DebugView, 0, 18),
            config.StudioWidth, config.StudioHeight, config.StudioDepth, config.StudioDensity,
            t * speed, config.StudioPitch, config.StudioRoll, 720f * config.StudioShapeScale,
            100f * config.StudioDetailScale, config.StudioErosion, config.StudioSampleSpacing, config.StudioLightSteps,
            tint.X, tint.Y, tint.Z, config.StudioCoverage,
            config.StudioMaxSteps, config.StudioJitter, config.StudioSearch, config.StudioTransmittance,
            config.StudioExtinction, config.StudioPhaseG, config.StudioPhaseGBack, config.StudioPhaseMix,
            config.StudioPowder, config.StudioAo, config.StudioAoPower, config.StudioMultiScatter,
            config.StudioSilver, config.StudioSunEnergy, config.StudioAmbient, (float)config.StudioSkyMode,
            config.StudioBaseSoft, config.StudioCrown, config.StudioEnvelope, config.StudioDetailType,
            (float)config.StudioNoise, config.StudioDensityPower, config.StudioHeightAo, config.StudioBeerCap,
            config.StudioAnvil, config.StudioShear, config.StudioLid, config.StudioBreakup,
            config.StudioCells, config.StudioAspect, config.StudioHeading, config.StudioFill,
            (float)config.StudioVolumeMode, (float)config.StudioFunnelShapeMode, config.StudioFunnelRadius, config.StudioFunnelHeight,
            config.StudioFunnelWall, t * config.StudioFunnelSpin, config.StudioFunnelSubX, config.StudioFunnelSubZ,
            (float)config.StudioFunnelStageMode, config.StudioFunnelSkirt, config.StudioFunnelIntensity, config.StudioFunnelWobble,
        };
    }
}
