using VRageMath;

namespace ClientPlugin.Clouds;

/// <summary>
/// Immutable game-thread to render-thread data. Published by <see cref="CloudSampler"/>,
/// consumed by <see cref="CloudRenderer"/>.
/// </summary>
public sealed class CloudSnapshot
{
    public readonly Vector3D PlanetCenter;
    public readonly float InnerRadius;
    public readonly float OuterRadius;
    public readonly Vector3 PlanetUp;
    public readonly Vector3 RotationAxis;
    public readonly float AngularVelocity;
    public readonly Vector3 Albedo;
    public readonly float FadeStartDistance;
    public readonly float FadeEndDistance;
    public readonly float MinScaledAltitude;
    public readonly float MaxHillRadius;
    public readonly float AtmosphereRadius;
    public readonly float FadeOutRelStart;
    public readonly float FadeOutRelEnd;
    public readonly int PlanetSeed;
    public readonly string WeatherTexture;

    public CloudSnapshot(
        Vector3D planetCenter,
        float innerRadius,
        float outerRadius,
        Vector3 planetUp,
        Vector3 rotationAxis,
        float angularVelocity,
        Vector3 albedo,
        float fadeStartDistance,
        float fadeEndDistance,
        float minScaledAltitude,
        float maxHillRadius,
        float atmosphereRadius,
        float fadeOutRelStart,
        float fadeOutRelEnd,
        int planetSeed,
        string weatherTexture)
    {
        PlanetCenter = planetCenter;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        PlanetUp = planetUp;
        RotationAxis = rotationAxis;
        AngularVelocity = angularVelocity;
        Albedo = albedo;
        FadeStartDistance = fadeStartDistance;
        FadeEndDistance = fadeEndDistance;
        MinScaledAltitude = minScaledAltitude;
        MaxHillRadius = maxHillRadius;
        AtmosphereRadius = atmosphereRadius;
        FadeOutRelStart = fadeOutRelStart;
        FadeOutRelEnd = fadeOutRelEnd;
        PlanetSeed = planetSeed;
        WeatherTexture = weatherTexture;
    }
}
