namespace ClientPlugin.Clouds;

/// <summary>
/// Game-thread diagnostics for the status screen. Updated with each snapshot.
/// </summary>
public sealed class CloudSamplerDiagnostics
{
    public bool Active;
    public string InactiveReason;
    public string PlanetName;
    public float CameraDistanceMeters;
    public int CloudLayerCount;
    public float TerrainRadius;
    public float HillRadius;
    public float AverageRadius;
    public float SeaRadius;
    public float AtmosphereRadius;
    public float AirTop;
    public float VisualCeil;
    public bool CeilFromAnomaly;
    public float InnerRadius;
    public float OuterRadius;
    public float Min01;
    public float Max01;
    public float TerrainHug;
    public string WeatherTexture;
    public VRageMath.Vector4 LayerPeaks;
    public VRageMath.Vector4 LayerWidths;
}

/// <summary>
/// Last render-thread pack dump. Zeroed when the pass is disabled.
/// </summary>
public sealed class CloudPackDiagnostics
{
    public bool Valid;
    public float Fade;
    public float CamToShell;
    public float Coverage;
    public float Spacing;
    public float Density;
    public float VolumeSize;
    public float ShapeScale;
    public float DetailScale;
    public float ErosionStrength;
    public float SampleSpacing;
    public float StepBudget;
    public float LightStepBudget;
    public float PackedHills;
    public float PackedAirTop;
    public float WeatherScale;
    public float Low;
    public float Mid;
    public float High;
    public float PackedInner;
    public float PackedOuter;
    public float PackedTerrain;
    public float AtmosphereFit;
    public bool HasDisplayTenant;
    public int DebugView;
}
