using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.World;
using VRage.Utils;
using VRageMath;
using VRageRender.Messages;

namespace ClientPlugin.Clouds;

/// <summary>
/// Game-thread side: nearest planet with CloudLayers, shell radii, fade, albedo.
/// Must never be called from the render thread.
/// </summary>
public static class CloudSampler
{
    const int UpdateInterval = 30;
    const double ExitRangeMargin = 2.0;

    static int frameCounter;
    static bool active;
    static long loggedPlanetId;

    public static void Update()
    {
        if (frameCounter++ % UpdateInterval != 0)
            return;

        CloudRenderer.Publish(BuildSnapshot());
    }

    public static void OnSessionUnloading()
    {
        active = false;
        loggedPlanetId = 0;
        CloudRenderer.Publish(null);
    }

    static void LogPlanet(MyPlanet planet, string detail)
    {
        if (planet.EntityId == loggedPlanetId)
            return;
        loggedPlanetId = planet.EntityId;
        MyLog.Default.Info($"{Plugin.Name}: nearest cloud planet '{planet.Generator?.Id.SubtypeName}': {detail}");
    }

    static CloudSnapshot BuildSnapshot()
    {
        var config = Config.Current;
        if (!config.Enabled)
        {
            active = false;
            return null;
        }

        if (MySession.Static == null || MySector.MainCamera == null || MyPlanets.Static == null)
        {
            active = false;
            return null;
        }

        var cameraPosition = MySector.MainCamera.Position;
        MyPlanet planet = null;
        double planetDistance = double.MaxValue;
        foreach (var candidate in MyPlanets.GetPlanets())
        {
            if (candidate == null || candidate.Closed)
                continue;
            var layers = candidate.Generator?.CloudLayers;
            if (layers == null || layers.Count == 0)
                continue;
            double candidateDistance = (cameraPosition - candidate.PositionComp.GetPosition()).Length();
            if (candidateDistance < planetDistance)
            {
                planetDistance = candidateDistance;
                planet = candidate;
            }
        }

        if (planet == null)
        {
            active = false;
            return null;
        }

        double fadeStartFactor = config.FadeStartFactor;
        double fadeEndFactor = Math.Max(config.FadeEndFactor, fadeStartFactor);
        var center = planet.PositionComp.GetPosition();
        float atmosphere = planet.AtmosphereRadius > 0f ? planet.AtmosphereRadius : planet.MaximumRadius * 1.2f;
        double range = atmosphere * (fadeEndFactor + (active ? ExitRangeMargin : 0.0));
        if (planetDistance > range)
        {
            active = false;
            return null;
        }

        active = true;

        DeriveShell(planet, config, atmosphere, out float inner, out float outer, out Vector3 albedo,
            out Vector3 rotationAxis, out float angularVelocity, out float fadeOutStart,
            out float fadeOutEnd, out string weatherTexture);

        var up = (Vector3)planet.WorldMatrix.Up;
        up.Normalize();
        if (rotationAxis.LengthSquared() < 1e-6f)
            rotationAxis = up;
        else
            rotationAxis.Normalize();

        float fadeStart = (float)(atmosphere * fadeStartFactor);
        float fadeEnd = (float)(atmosphere * fadeEndFactor);
        float minScaled = (planet.AverageRadius + planet.MaximumRadius) * 0.5f;
        int seed = planet.Generator?.Id.SubtypeName?.GetHashCode() ?? (int)planet.EntityId;
        if (!string.IsNullOrEmpty(weatherTexture))
            seed ^= weatherTexture.GetHashCode();

        LogPlanet(planet, $"shell {inner / 1000f:0.#}-{outer / 1000f:0.#} km, layers {planet.Generator.CloudLayers.Count}");

        return new CloudSnapshot(center, inner, outer, up, rotationAxis, angularVelocity, albedo,
            fadeStart, fadeEnd, minScaled, planet.MaximumRadius, atmosphere, fadeOutStart, fadeOutEnd, seed,
            weatherTexture);
    }

    static void DeriveShell(MyPlanet planet, Config config, float atmosphere,
        out float inner, out float outer,
        out Vector3 albedo, out Vector3 rotationAxis, out float angularVelocity,
        out float fadeOutStart, out float fadeOutEnd, out string weatherTexture)
    {
        double midHill = (planet.AverageRadius + planet.MaximumRadius) * 0.5;
        double minAlt = double.MaxValue;
        double maxAlt = 0;
        var colorSum = Vector4.Zero;
        int colorCount = 0;
        rotationAxis = Vector3.Zero;
        angularVelocity = 0f;
        fadeOutStart = 0f;
        fadeOutEnd = 0f;
        weatherTexture = null;
        int axisCount = 0;

        foreach (MyCloudLayerSettings layer in planet.Generator.CloudLayers)
        {
            if (layer == null)
                continue;
            double altitude = midHill + (planet.MaximumRadius - midHill) * layer.RelativeAltitude;
            minAlt = Math.Min(minAlt, altitude);
            maxAlt = Math.Max(maxAlt, altitude);
            colorSum += layer.Color.ToLinearRGB();
            colorCount++;
            fadeOutStart += layer.FadeOutRelativeAltitudeStart;
            fadeOutEnd += layer.FadeOutRelativeAltitudeEnd;
            if (weatherTexture == null && layer.Textures != null && layer.Textures.Count > 0)
                weatherTexture = layer.Textures[0];
            if (layer.RotationAxis != Vector3D.Zero)
            {
                rotationAxis += (Vector3)Vector3D.Normalize(layer.RotationAxis);
                axisCount++;
            }
            angularVelocity += layer.AngularVelocity;
        }

        if (minAlt >= maxAlt)
        {
            minAlt = planet.MaximumRadius * 0.98;
            maxAlt = planet.MaximumRadius * 1.02;
        }

        // Geometric AtmosphereRadius is outside the visual scattering limb.
        // Never author a shell past that ceiling (and never drop the ceiling
        // when the Keen CloudLayer altitudes already sit near it).
        float atmoCeil = atmosphere > planet.MaximumRadius + 200f
            ? atmosphere * 0.90f
            : planet.MaximumRadius * 1.06f;
        float thickness = Math.Max(config.Thickness, 0.01f) * planet.AverageRadius;
        float room = Math.Max(atmoCeil - planet.MaximumRadius - 80f, 400f);
        thickness = Math.Min(thickness, room);

        inner = (float)Math.Max(minAlt - thickness * 0.15, planet.MaximumRadius + 80.0);
        inner = Math.Min(inner, atmoCeil - 400f);
        inner = Math.Max(inner, planet.MaximumRadius + 80f);

        float deck = Math.Max(Math.Min(thickness, atmoCeil - inner), 400f);
        float deckOuter = (float)Math.Max(Math.Min(maxAlt + deck * 0.25, atmoCeil), inner + deck);
        outer = Math.Min(deckOuter, atmoCeil);
        if (outer < inner + 200f)
            outer = Math.Min(inner + 400f, atmoCeil);
        if (inner >= outer)
        {
            inner = Math.Max(atmoCeil - 800f, planet.MaximumRadius + 80f);
            outer = atmoCeil;
        }

        albedo = colorCount > 0
            ? new Vector3(colorSum.X, colorSum.Y, colorSum.Z) / colorCount
            : Vector3.One;
        if (albedo.LengthSquared() < 1e-4f)
            albedo = Vector3.One;
        if (axisCount > 0)
            rotationAxis /= axisCount;
        if (planet.Generator.CloudLayers.Count > 0)
        {
            angularVelocity /= planet.Generator.CloudLayers.Count;
            fadeOutStart /= planet.Generator.CloudLayers.Count;
            fadeOutEnd /= planet.Generator.CloudLayers.Count;
        }
    }
}
