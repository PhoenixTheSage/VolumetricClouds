using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Clouds;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using VRageMath;

namespace ClientPlugin;

public enum CloudQuality
{
    Low,
    Medium,
    High,
    Ultra
}

/// <summary>IsolatedMix false-color. Packed as lightSteps + mode*16 on b7.</summary>
/// <summary>How the studio 128³ tile is turned into the up-rez signal. The profile is still the cloud.</summary>
public enum StudioNoiseMode
{
    PerlinWorley = 0,
    Billow = 1,
    Worley = 2,
    InvertedWorley = 3,
}

public enum StudioSky
{
    Blue = 0,
    White = 1,
}

/// <summary>Same order as the planetary type ids. A genus is a profile preset, not a noise tile.</summary>
public enum StudioVolume
{
    Cloud = 0,
    Funnel = 1,
}

public enum StudioFunnelShape
{
    Rope = 0,
    Cone = 1,
    Stovepipe = 2,
    Wedge = 3,
}

public enum StudioFunnelStage
{
    Aloft = 0,
    Ground = 1,
    RopeOut = 2,
}

public enum StudioGenus
{
    Stratus = 0,
    Stratocumulus = 1,
    Cumulus = 2,
    Nimbostratus = 3,
    Cumulonimbus = 4,
    Altostratus = 5,
    Altocumulus = 6,
    Cirrus = 7,
    Cirrostratus = 8,
    Cirrocumulus = 9,
}

public enum CloudDebugView
{
    Off = 0,
    WeatherCoverage = 1,
    WeatherType = 2,
    WeatherUv = 3,
    WeatherMap = 4,
    Altitude = 5,
    HeightProfile = 6,
    CoverageAmount = 7,
    ShapeNoise = 8,
    DensityBase = 9,
    DensityEroded = 10,
    StormBody = 11,
    MarchSteps = 13,
    MarchCoarseFine = 14,
    MarchEmpty = 15,
    Transmittance = 16,
    SamplePos = 17,
    ViewLod = 18,
}

public class Config : INotifyPropertyChanged
{
    #region Options

    private bool enabled = true;
    private bool volumeStudio = true;
    private bool replaceVanilla = true;
    private CloudQuality quality = CloudQuality.Medium;
    private float coverage = 1f;
    private float spacing = 0.7f;
    private float density = 1f;
    private float thickness = 0.06f;
    private float baseAltitude = 0.23f;
    private float maxAltitude = 0.75f;
    private float atmosphereFit = 0.35f;
    private float windSpeed = 1.0f;
    private float lowCoverage = 1.25f;
    private float midCoverage = 0.65f;
    private float cirrusStrength = 0.16f;
    private float lowHeight = 0.22f;
    private float midHeight = 0.48f;
    private float highHeight = 0.78f;
    private float[] typeThickness = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    private float[] typeShape = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    private float shapeScale = 1f;
    private float detailScale = 1f;
    private float erosionStrength = 1f;
    private float sampleSpacing = 24f;
    private Color albedoTint = Color.White;
    private float hdrLift = 4f;
    private float horizonRange = 2.5f;
    private float fadeStartFactor = 7.3f;
    private float fadeEndFactor = 15f;
    private CloudDebugView debugView = CloudDebugView.Off;
    private float studioDistance = 2600f;
    private float studioWidth = 1000f;
    private float studioHeight = 520f;
    private float studioDepth = 1000f;
    private float studioLift = 0f;
    private float studioYaw = 0f;
    private float studioPitch = 0f;
    private float studioRoll = 0f;
    private float studioSampleSpacing = 24f;
    private int studioMaxSteps = 64;
    private int studioLightSteps = 3;
    private float studioJitter = 1f;
    private float studioSearch = 4f;
    private float studioTransmittance = 0.02f;
    private StudioNoiseMode studioNoise = StudioNoiseMode.PerlinWorley;
    private float studioShapeScale = 1f;
    private float studioDetailScale = 1f;
    private float studioErosion = 1f;
    private float studioCoverage = 1f;
    private float studioDensity = 1f;
    private float studioDensityPower = 1f;
    private float studioWind = 1f;
    private float studioDetailType = 0.65f;
    private float studioExtinction = 0.01f;
    private float studioPhaseG = 0.55f;
    private float studioPhaseGBack = -0.2f;
    private float studioPhaseMix = 0.7f;
    private float studioPowder = 0.35f;
    private float studioAo = 1f;
    private float studioAoPower = 0.5f;
    private float studioHeightAo = 0.45f;
    private float studioMultiScatter = 0.4f;
    private float studioSilver = 0.25f;
    private float studioSunEnergy = 1f;
    private float studioAmbient = 1f;
    private float studioBeerCap = 1.35f;
    private StudioSky studioSky = StudioSky.Blue;
    private float studioBaseSoft = 0.16f;
    private float studioCrown = 0.58f;
    private float studioEnvelope = 0.78f;
    private float studioAnvil = 0f;
    private float studioShear = 0f;
    private float studioLid = 0.1f;
    private float studioBreakup = 0.2f;
    private float studioCells = 2f;
    private float studioAspect = 1f;
    private float studioHeading = 0f;
    private float studioFill = 0.15f;
    private StudioVolume studioVolume = StudioVolume.Cloud;
    private StudioFunnelShape studioFunnelShape = StudioFunnelShape.Cone;
    private StudioFunnelStage studioFunnelStage = StudioFunnelStage.Ground;
    private float studioFunnelRadius = 180f;
    private float studioFunnelHeight = 0.85f;
    private float studioFunnelWall = 0.35f;
    private float studioFunnelSpin = 0.8f;
    private float studioFunnelWobble = 40f;
    private float studioFunnelIntensity = 1f;
    private float studioFunnelSkirt = 0.6f;
    private float studioFunnelSubX = 80f;
    private float studioFunnelSubZ = -40f;
    private StudioGenus studioGenus = StudioGenus.Cumulus;
    private StudioGenus studioMorphTarget = StudioGenus.Cumulonimbus;
    private float studioMorph = 0f;

    #endregion

    #region User interface

    public readonly string Title = "Volumetric Clouds";

    [Separator("Volumetric Clouds")]

    [Checkbox(description: "Master switch for raymarched clouds")]
    public bool Enabled
    {
        get => enabled;
        set => SetField(ref enabled, value);
    }

    public bool VolumeStudio
    {
        get => volumeStudio;
        set => SetField(ref volumeStudio, value);
    }

    [Checkbox(label: "Replace vanilla layers", description: "Hide Keen CloudSphere layers while this pack is drawing")]
    public bool ReplaceVanilla
    {
        get => replaceVanilla;
        set => SetField(ref replaceVanilla, value);
    }

    [Dropdown(description: "Raymarching quality (samples per pixel)")]
    public CloudQuality Quality
    {
        get => quality;
        set => SetField(ref quality, value);
    }

    [Slider(0.05f, 2f, 0.01f, SliderAttribute.SliderType.Float, label: "Amount", description: "HZD fill inside a weather system (3D shape threshold). Does not cut puff silhouettes.")]
    public float Coverage
    {
        get => coverage;
        set => SetField(ref coverage, value);
    }

    [Slider(0.35f, 3f, 0.05f, SliderAttribute.SliderType.Float, label: "Spacing", description: "Storm-track breakup on the global climate map. 1 = a few systems around the planet. Does not tile coverage.")]
    public float Spacing
    {
        get => spacing;
        set => SetField(ref spacing, value);
    }

    [Slider(0.05f, 1.2f, 0.01f, SliderAttribute.SliderType.Float, description: "Cloud optical density")]
    public float Density
    {
        get => density;
        set => SetField(ref density, value);
    }

    [Separator("Height (0 = sea / ground, 1 = extras air top × fit, >1 High/Cb overshoot)")]

    [Slider(0f, 5f, 0.01f, SliderAttribute.SliderType.Float, label: "Min height", description: "Same unit as Max. 0 = sea. 1 = the fitted limb. Hides type decks below the floor. Does not stretch them or turn the floor into a shell.")]
    public float BaseAltitude
    {
        get => baseAltitude;
        set => SetField(ref baseAltitude, value);
    }

    [Slider(0f, 5f, 0.01f, SliderAttribute.SliderType.Float, label: "Max height", description: "Same unit as Min. 1 = hills blended toward extras air top by Atmosphere fit. Above 1 continues past that lid in the same metres. Does not stretch type decks.")]
    public float MaxAltitude
    {
        get => maxAltitude;
        set => SetField(ref maxAltitude, value);
    }

    [Slider(0.35f, 1f, 0.01f, SliderAttribute.SliderType.Float, label: "Atmosphere fit", description: "At Max 1, how far IsolatedMix reaches toward extras air top. Lower hugs the visible blue limb. 1 = gameplay air top (past the rim until Anomaly Slice AP).")]
    public float AtmosphereFit
    {
        get => atmosphereFit;
        set => SetField(ref atmosphereFit, value);
    }

    /// <summary>Kept for old Clouds.cfg. Height is Min/Max now.</summary>
    public float Thickness
    {
        get => thickness;
        set => SetField(ref thickness, value);
    }

    [Slider(0f, 8f, 0.05f, SliderAttribute.SliderType.Float, label: "Wind speed", description: "Advection of the live climate field. 1 carries storms a few degrees of longitude per real minute. Shape noise keeps its own drift.")]
    public float WindSpeed
    {
        get => windSpeed;
        set => SetField(ref windSpeed, value);
    }

    [Separator("Weather fields")]

    /// <summary>Unused leftover. Old Clouds.cfg still deserializes it.</summary>
    public float LowHeight
    {
        get => lowHeight;
        set => SetField(ref lowHeight, value);
    }

    [Slider(0f, 1.5f, 0.01f, SliderAttribute.SliderType.Float, label: "Convective", description: "Convective field amount: stratus through cumulonimbus, plus alto")]
    public float LowCoverage
    {
        get => lowCoverage;
        set => SetField(ref lowCoverage, value);
    }

    /// <summary>Unused leftover. Old Clouds.cfg still deserializes it.</summary>
    public float MidHeight
    {
        get => midHeight;
        set => SetField(ref midHeight, value);
    }

    /// <summary>Unused leftover. Old Clouds.cfg still deserializes it.</summary>
    public float MidCoverage
    {
        get => midCoverage;
        set => SetField(ref midCoverage, value);
    }

    /// <summary>Unused leftover. Old Clouds.cfg still deserializes it.</summary>
    public float HighHeight
    {
        get => highHeight;
        set => SetField(ref highHeight, value);
    }

    /// <summary>Cirrus sheet amount. No control until the ice types are redesigned. Old Clouds.cfg name stays so CirrusStrength loads.</summary>
    public float CirrusStrength
    {
        get => cirrusStrength;
        set => SetField(ref cirrusStrength, value);
    }

    /// <summary>Per-type vertical extent scale. XML array; Rich HUD type pages only.</summary>
    public float[] TypeThickness
    {
        get => typeThickness;
        set => typeThickness = Pad10(value, 1f);
    }

    /// <summary>Per-type voxel puff scale. XML array; Rich HUD type pages only.</summary>
    public float[] TypeShape
    {
        get => typeShape;
        set => typeShape = Pad10(value, 1f);
    }

    public float TypeThicknessAt(int index) => ScaleAt(typeThickness, index, 0.25f, 2f);

    public float TypeShapeAt(int index) => ScaleAt(typeShape, index, 0.35f, 2f);

    public void SetTypeThickness(int index, float value) => SetScale(ref typeThickness, index, value, 0.25f, 2f);

    public void SetTypeShape(int index, float value) => SetScale(ref typeShape, index, value, 0.35f, 2f);

    [Separator("Density model")]

    [Slider(0.5f, 3f, 0.05f, SliderAttribute.SliderType.Float, label: "Base shape size", description: "HZD/Evolved 128³ wrap. 1 = 4 km (Worley-4 ≈ 1 km puffs). Cubed 100 m is Detail size, not this.")]
    public float ShapeScale
    {
        get => shapeScale;
        set => SetField(ref shapeScale, value);
    }

    [Slider(0.25f, 4f, 0.05f, SliderAttribute.SliderType.Float, label: "Detail size", description: "Cubed GetUprezzed scale (inSamplePosition×0.01 ≈ 100 m). Worley erosion on the Evolved body.")]
    public float DetailScale
    {
        get => detailScale;
        set => SetField(ref detailScale, value);
    }

    [Slider(0f, 2f, 0.02f, SliderAttribute.SliderType.Float, label: "Erosion", description: "Worley edge erosion strength. 0 disables fine erosion; 1 = researched default.")]
    public float ErosionStrength
    {
        get => erosionStrength;
        set => SetField(ref erosionStrength, value);
    }

    [Slider(20f, 120f, 2f, SliderAttribute.SliderType.Float, label: "Sample spacing", description: "Fine raymarch step in metres (Nubis near). Default 24 m matches ~260 m Perlin-Worley period. Coarse search skips empty air.")]
    public float SampleSpacing
    {
        get => sampleSpacing;
        set => SetField(ref sampleSpacing, value);
    }

    [Color(description: "Multiplies the planet CloudLayer albedo")]
    public Color AlbedoTint
    {
        get => albedoTint;
        set => SetField(ref albedoTint, value);
    }

    [Slider(1f, 16f, 0.5f, SliderAttribute.SliderType.Float, label: "HDR lift", description: "Extra brightness when an HDR Display pack is live. 1 = same as SDR.")]
    public float HdrLift
    {
        get => hdrLift;
        set => SetField(ref hdrLift, value);
    }

    [Separator("Horizon")]

    [Slider(0.5f, 8f, 0.1f, SliderAttribute.SliderType.Float, label: "Horizon range", description: "Max view distance for clouds, in atmosphere radii. Does not change Min/Max height or Atmosphere fit.")]
    public float HorizonRange
    {
        get => horizonRange;
        set => SetField(ref horizonRange, value);
    }

    /// <summary>Legacy xml only. Migrated to <see cref="HorizonRange"/>.</summary>
    public float FadeStartFactor
    {
        get => fadeStartFactor;
        set => fadeStartFactor = value;
    }

    /// <summary>Legacy xml only. Migrated to <see cref="HorizonRange"/>.</summary>
    public float FadeEndFactor
    {
        get => fadeEndFactor;
        set => fadeEndFactor = value;
    }

    [Separator("Debug")]

    [Dropdown(label: "View", description: "False-color IsolatedMix. Weather / density / march. Off = shaded clouds.")]
    public CloudDebugView DebugView
    {
        get => debugView;
        set => SetField(ref debugView, value);
    }

    [Button(label: "Show Status", description: "Shell radii, packed VolumeHdr, textures, Anomaly program/catalog — prefer over the HUD overlay")]
    public static void ShowStatus() => Clouds.CloudsStatus.Show();

    #endregion

    #region Volume studio

    public float StudioDistance { get => studioDistance; set => SetStudio(ref studioDistance, value, 600f, 8000f); }
    public float StudioWidth { get => studioWidth; set => SetStudio(ref studioWidth, value, 200f, 4000f); }
    public float StudioHeight { get => studioHeight; set => SetStudio(ref studioHeight, value, 80f, 2000f); }
    public float StudioDepth { get => studioDepth; set => SetStudio(ref studioDepth, value, 200f, 4000f); }
    public float StudioLift { get => studioLift; set => SetStudio(ref studioLift, value, -800f, 800f); }
    public float StudioYaw { get => studioYaw; set => SetStudio(ref studioYaw, value, -180f, 180f); }
    public float StudioPitch { get => studioPitch; set => SetStudio(ref studioPitch, value, -180f, 180f); }
    public float StudioRoll { get => studioRoll; set => SetStudio(ref studioRoll, value, -180f, 180f); }
    public float StudioSampleSpacing { get => studioSampleSpacing; set => SetStudio(ref studioSampleSpacing, value, 12f, 120f); }
    public int StudioMaxSteps { get => studioMaxSteps; set => SetStudioInt(ref studioMaxSteps, value, 16, 96); }
    public int StudioLightSteps { get => studioLightSteps; set => SetStudioInt(ref studioLightSteps, value, 1, 6); }
    public float StudioJitter { get => studioJitter; set => SetStudio(ref studioJitter, value, 0f, 1f); }
    public float StudioSearch { get => studioSearch; set => SetStudio(ref studioSearch, value, 1f, 8f); }
    public float StudioTransmittance { get => studioTransmittance; set => SetStudio(ref studioTransmittance, value, 0.005f, 0.2f); }
    public StudioNoiseMode StudioNoise { get => studioNoise; set => SetField(ref studioNoise, value); }
    public float StudioShapeScale { get => studioShapeScale; set => SetStudio(ref studioShapeScale, value, 0.2f, 6f); }
    public float StudioDetailScale { get => studioDetailScale; set => SetStudio(ref studioDetailScale, value, 0.1f, 8f); }
    public float StudioErosion { get => studioErosion; set => SetStudio(ref studioErosion, value, 0f, 4f); }
    public float StudioCoverage { get => studioCoverage; set => SetStudio(ref studioCoverage, value, 0.05f, 2f); }
    public float StudioDensity { get => studioDensity; set => SetStudio(ref studioDensity, value, 0.05f, 2f); }
    public float StudioDensityPower { get => studioDensityPower; set => SetStudio(ref studioDensityPower, value, 0.4f, 2f); }
    public float StudioWind { get => studioWind; set => SetStudio(ref studioWind, value, 0f, 8f); }
    public float StudioDetailType { get => studioDetailType; set => SetStudio(ref studioDetailType, value, 0f, 1f); }
    public float StudioExtinction { get => studioExtinction; set => SetStudio(ref studioExtinction, value, 0.002f, 0.08f); }
    public float StudioPhaseG { get => studioPhaseG; set => SetStudio(ref studioPhaseG, value, 0f, 0.95f); }
    public float StudioPhaseGBack { get => studioPhaseGBack; set => SetStudio(ref studioPhaseGBack, value, -0.6f, 0.2f); }
    public float StudioPhaseMix { get => studioPhaseMix; set => SetStudio(ref studioPhaseMix, value, 0f, 1f); }
    public float StudioPowder { get => studioPowder; set => SetStudio(ref studioPowder, value, 0f, 1f); }
    public float StudioAo { get => studioAo; set => SetStudio(ref studioAo, value, 0f, 1f); }
    public float StudioAoPower { get => studioAoPower; set => SetStudio(ref studioAoPower, value, 0.2f, 2f); }
    public float StudioHeightAo { get => studioHeightAo; set => SetStudio(ref studioHeightAo, value, 0f, 1f); }
    public float StudioMultiScatter { get => studioMultiScatter; set => SetStudio(ref studioMultiScatter, value, 0f, 1f); }
    public float StudioSilver { get => studioSilver; set => SetStudio(ref studioSilver, value, 0f, 1.5f); }
    public float StudioSunEnergy { get => studioSunEnergy; set => SetStudio(ref studioSunEnergy, value, 0f, 4f); }
    public float StudioAmbient { get => studioAmbient; set => SetStudio(ref studioAmbient, value, 0f, 4f); }
    public float StudioBeerCap { get => studioBeerCap; set => SetStudio(ref studioBeerCap, value, 0.4f, 4f); }
    public StudioSky StudioSkyMode { get => studioSky; set => SetField(ref studioSky, value); }
    public float StudioBaseSoft { get => studioBaseSoft; set => SetStudio(ref studioBaseSoft, value, 0.02f, 0.5f); }
    public float StudioCrown { get => studioCrown; set => SetStudio(ref studioCrown, value, 0.2f, 0.98f); }
    public float StudioEnvelope { get => studioEnvelope; set => SetStudio(ref studioEnvelope, value, 0.15f, 1.6f); }
    public float StudioAnvil { get => studioAnvil; set => SetStudio(ref studioAnvil, value, 0f, 1f); }
    public float StudioShear { get => studioShear; set => SetStudio(ref studioShear, value, -1.5f, 1.5f); }
    public float StudioLid { get => studioLid; set => SetStudio(ref studioLid, value, 0f, 1f); }
    public float StudioBreakup { get => studioBreakup; set => SetStudio(ref studioBreakup, value, 0f, 1f); }
    public float StudioCells { get => studioCells; set => SetStudio(ref studioCells, value, 1f, 8f); }
    public float StudioAspect { get => studioAspect; set => SetStudio(ref studioAspect, value, 0.15f, 4f); }
    public float StudioHeading { get => studioHeading; set => SetStudio(ref studioHeading, value, 0f, 6.28f); }
    public float StudioFill { get => studioFill; set => SetStudio(ref studioFill, value, 0f, 1f); }

    public StudioVolume StudioVolumeMode { get => studioVolume; set => SetField(ref studioVolume, value); }
    public StudioFunnelShape StudioFunnelShapeMode { get => studioFunnelShape; set => SetField(ref studioFunnelShape, value); }
    public StudioFunnelStage StudioFunnelStageMode { get => studioFunnelStage; set => SetField(ref studioFunnelStage, value); }
    public float StudioFunnelRadius { get => studioFunnelRadius; set => SetStudio(ref studioFunnelRadius, value, 20f, 800f); }
    public float StudioFunnelHeight { get => studioFunnelHeight; set => SetStudio(ref studioFunnelHeight, value, 0.2f, 1f); }
    public float StudioFunnelWall { get => studioFunnelWall; set => SetStudio(ref studioFunnelWall, value, 0.05f, 1f); }
    public float StudioFunnelSpin { get => studioFunnelSpin; set => SetStudio(ref studioFunnelSpin, value, 0f, 4f); }
    public float StudioFunnelWobble { get => studioFunnelWobble; set => SetStudio(ref studioFunnelWobble, value, 0f, 200f); }
    public float StudioFunnelIntensity { get => studioFunnelIntensity; set => SetStudio(ref studioFunnelIntensity, value, 0f, 2f); }
    public float StudioFunnelSkirt { get => studioFunnelSkirt; set => SetStudio(ref studioFunnelSkirt, value, 0f, 1f); }
    public float StudioFunnelSubX { get => studioFunnelSubX; set => SetStudio(ref studioFunnelSubX, value, -400f, 400f); }
    public float StudioFunnelSubZ { get => studioFunnelSubZ; set => SetStudio(ref studioFunnelSubZ, value, -400f, 400f); }

    public StudioGenus StudioGenusFrom
    {
        get => studioGenus;
        set
        {
            if (!SetField(ref studioGenus, value))
                return;
            StudioGenera.Apply(this);
        }
    }

    public StudioGenus StudioMorphTarget
    {
        get => studioMorphTarget;
        set
        {
            if (!SetField(ref studioMorphTarget, value))
                return;
            StudioGenera.Apply(this);
        }
    }

    public float StudioMorph
    {
        get => studioMorph;
        set
        {
            if (value < 0f) value = 0f;
            if (value > 1f) value = 1f;
            if (!SetField(ref studioMorph, value))
                return;
            StudioGenera.Apply(this);
        }
    }

    void SetStudio(ref float field, float value, float min, float max)
    {
        if (value < min) value = min;
        if (value > max) value = max;
        SetField(ref field, value);
    }

    void SetStudioInt(ref int field, int value, int min, int max)
    {
        if (value < min) value = min;
        if (value > max) value = max;
        SetField(ref field, value);
    }

    #endregion

    #region Derived values

    // Nubis 2017 uses 54–96 view samples; Nubis Evolved uses 96–180 at
    // full resolution. The coarse/fine marcher makes these upper bounds,
    // not a uniform cost through empty air.
    public const int MaxRaymarchSteps = 160;

    public int StepCount
    {
        get
        {
            switch (quality)
            {
                case CloudQuality.Low:
                    return 64;
                case CloudQuality.Medium:
                    return 96;
                case CloudQuality.Ultra:
                    return MaxRaymarchSteps;
                default:
                    return 128;
            }
        }
    }

    public int LightStepCount
    {
        get
        {
            switch (quality)
            {
                case CloudQuality.Low:
                    return 4;
                case CloudQuality.Medium:
                    return 5;
                case CloudQuality.Ultra:
                    return 6;
                default:
                    return 6;
            }
        }
    }

    #endregion

    static float[] Pad10(float[] value, float fill)
    {
        var next = new float[10];
        for (int i = 0; i < 10; i++)
            next[i] = fill;
        if (value == null)
            return next;
        int n = value.Length < 10 ? value.Length : 10;
        for (int i = 0; i < n; i++)
            next[i] = value[i];
        return next;
    }

    static float ScaleAt(float[] values, int index, float min, float max)
    {
        if (values == null || index < 0 || index >= values.Length)
            return 1f;
        var v = values[index];
        if (v < min)
            return min;
        if (v > max)
            return max;
        return v;
    }

    void SetScale(ref float[] values, int index, float value, float min, float max)
    {
        if (index < 0 || index > 9)
            return;
        values = Pad10(values, 1f);
        if (value < min)
            value = min;
        if (value > max)
            value = max;
        if (Math.Abs(values[index] - value) < 1e-5f)
            return;
        values[index] = value;
        if (ReferenceEquals(this, Current))
            ConfigStorage.Save(this);
    }

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (ReferenceEquals(this, Current))
        {
            ConfigStorage.Save(this);
            if (propertyName == nameof(BaseAltitude) ||
                propertyName == nameof(MaxAltitude) ||
                propertyName == nameof(AtmosphereFit) ||
                propertyName == nameof(Enabled))
                CloudSampler.Invalidate();
        }
        return true;
    }

    #endregion
}
