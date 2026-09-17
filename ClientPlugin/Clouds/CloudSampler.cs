using System;
using System.Collections.Generic;
using ClientPlugin.Anomaly;
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
    static bool dirty = true;
    static long loggedPlanetId;
    static float loggedInner;
    static float loggedOuter;
    static string hudLine;

    /// <summary>Cached HUD/overlay line. HUD draw thread reads this; game thread writes.</summary>
    public static string HudLine => hudLine;

    public static void Invalidate()
    {
        dirty = true;
    }

    public static void Update()
    {
        if (!dirty && frameCounter++ % UpdateInterval != 0)
            return;
        dirty = false;

        CloudRenderer.Publish(BuildSnapshot());
    }

    public static void OnSessionUnloading()
    {
        active = false;
        dirty = true;
        loggedPlanetId = 0;
        loggedInner = 0f;
        loggedOuter = 0f;
        hudLine = null;
        CloudRenderer.Publish(null);
    }

    static void LogPlanet(MyPlanet planet, float inner, float outer, string detail)
    {
        if (planet.EntityId == loggedPlanetId &&
            Math.Abs(inner - loggedInner) < 40f &&
            Math.Abs(outer - loggedOuter) < 40f)
            return;
        loggedPlanetId = planet.EntityId;
        loggedInner = inner;
        loggedOuter = outer;
        MyLog.Default.Info($"{Plugin.Name}: nearest cloud planet '{planet.Generator?.Id.SubtypeName}': {detail}");
    }

    static CloudSnapshot BuildSnapshot()
    {
        var config = Config.Current;
        if (!config.Enabled)
        {
            active = false;
            hudLine = null;
            return null;
        }

        if (MySession.Static == null || MySector.MainCamera == null || MyPlanets.Static == null)
        {
            active = false;
            hudLine = null;
            return null;
        }

        var cameraPosition = MySector.MainCamera.Position;
        MyPlanet planet = null;
        MyPlanet atmospherePlanet = null;
        double planetDistance = double.MaxValue;
        double atmosphereDistance = double.MaxValue;
        foreach (var candidate in MyPlanets.GetPlanets())
        {
            if (candidate == null || candidate.Closed)
                continue;
            double candidateDistance = (cameraPosition - candidate.PositionComp.GetPosition()).Length();
            var layers = candidate.Generator?.CloudLayers;
            if (layers != null && layers.Count > 0 && candidateDistance < planetDistance)
            {
                planetDistance = candidateDistance;
                planet = candidate;
            }

            if (candidate.HasAtmosphere && candidateDistance < atmosphereDistance)
            {
                atmosphereDistance = candidateDistance;
                atmospherePlanet = candidate;
            }
        }

        if (planet == null)
        {
            planet = atmospherePlanet;
            planetDistance = atmosphereDistance;
        }

        if (planet == null)
        {
            active = false;
            hudLine = null;
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
            hudLine = null;
            return null;
        }

        active = true;

        DeriveShell(planet, config, out float inner, out float outer, out float visualCeil, out Vector3 albedo,
            out Vector3 rotationAxis, out float angularVelocity, out float fadeOutStart,
            out float fadeOutEnd, out string weatherTexture,
            out Vector4 layerPeaks, out Vector4 layerWidths, out float terrainHug);

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

        int layerCount = planet.Generator?.CloudLayers?.Count ?? 0;
        LogPlanet(planet, inner, outer,
            $"shell {inner / 1000f:0.#}-{outer / 1000f:0.#} km, hills {planet.MaximumRadius / 1000f:0.#} km, ceil {visualCeil / 1000f:0.#} km, layers {layerCount}");
        hudLine = $"{planet.Generator?.Id.SubtypeName ?? "planet"}  {inner / 1000f:0.#}–{outer / 1000f:0.#} km  (hills {planet.MaximumRadius / 1000f:0.#}, ceil {visualCeil / 1000f:0.#})";

        return new CloudSnapshot(center, inner, outer, up, rotationAxis, angularVelocity, albedo,
            fadeStart, fadeEnd, minScaled, planet.MaximumRadius, atmosphere, fadeOutStart, fadeOutEnd, seed,
            weatherTexture, layerPeaks, layerWidths, terrainHug);
    }

    static void DeriveShell(MyPlanet planet, Config config,
        out float inner, out float outer, out float visualCeil,
        out Vector3 albedo, out Vector3 rotationAxis, out float angularVelocity,
        out float fadeOutStart, out float fadeOutEnd, out string weatherTexture,
        out Vector4 layerPeaks, out Vector4 layerWidths, out float terrainHug)
    {
        var colorSum = Vector4.Zero;
        int colorCount = 0;
        rotationAxis = Vector3.Zero;
        angularVelocity = 0f;
        fadeOutStart = 0f;
        fadeOutEnd = 0f;
        weatherTexture = null;
        int axisCount = 0;
        var rels = new List<float>(4);
        var layers = planet.Generator?.CloudLayers;

        if (layers != null)
        {
            foreach (MyCloudLayerSettings layer in layers)
            {
                if (layer == null)
                    continue;
            // RelativeAltitude is hill amplitude on Keen CloudSphere. Remap
            // it to 0–1 peaks inside the allowed column (not meters AGL).
            colorSum += layer.Color.ToLinearRGB();
            colorCount++;
            fadeOutStart += layer.FadeOutRelativeAltitudeStart;
            fadeOutEnd += layer.FadeOutRelativeAltitudeEnd;
            if (rels.Count < 4)
                rels.Add(layer.RelativeAltitude);
            if (weatherTexture == null && layer.Textures != null && layer.Textures.Count > 0)
                weatherTexture = layer.Textures[0];
            if (layer.RotationAxis != Vector3D.Zero)
            {
                rotationAxis += (Vector3)Vector3D.Normalize(layer.RotationAxis);
                axisCount++;
            }
            angularVelocity += layer.AngularVelocity;
            }
        }

        float hillTop = planet.MaximumRadius;
        float avg = planet.AverageRadius;
        float terrain = planet.MinimumRadius > avg * 0.5f ? planet.MinimumRadius : avg;
        var center = planet.PositionComp.GetPosition();
        // Slice AK extras when this planet matches Anomaly's snapshot.
        // Fail closed to the same formula as PlanetAtmosphere.TryComputeRadii
        // (air top, not 0.90× / 0.72× AtmosphereRadius).
        float airTop;
        if (!AnomalyBridge.TryGetCeilings(center, out airTop, out visualCeil) ||
            visualCeil <= terrain + 80f)
            ComputeCeilings(planet, avg, terrain, hillTop, out airTop, out visualCeil);

        float min01 = MathHelper.Clamp(config.BaseAltitude, 0f, 1f);
        float max01 = MathHelper.Clamp(config.MaxAltitude, 0f, 1f);
        if (max01 < min01 + 0.05f)
            max01 = Math.Min(min01 + 0.05f, 1f);
        float span = Math.Max(visualCeil - terrain, 200f);
        inner = terrain + span * min01;
        outer = Math.Min(terrain + span * max01, visualCeil);
        if (outer < inner + 80f)
            outer = Math.Min(inner + 200f, visualCeil);
        if (inner >= outer)
        {
            inner = terrain;
            outer = visualCeil;
        }

        terrainHug = inner <= hillTop + 50f ? 1f : 0f;
        BuildLayerBands(rels, out layerPeaks, out layerWidths);

        albedo = colorCount > 0
            ? new Vector3(colorSum.X, colorSum.Y, colorSum.Z) / colorCount
            : Vector3.One;
        if (albedo.LengthSquared() < 1e-4f)
            albedo = Vector3.One;
        if (axisCount > 0)
            rotationAxis /= axisCount;
        int layerCount = layers?.Count ?? 0;
        if (layerCount > 0)
        {
            angularVelocity /= layerCount;
            fadeOutStart /= layerCount;
            fadeOutEnd /= layerCount;
        }
    }

    // Same as ClientPlugin.Shaders.PlanetAtmosphere.TryComputeRadii.
    static void ComputeCeilings(MyPlanet planet, float avg, float terrain, float hill,
        out float airTop, out float visualCeil)
    {
        if (avg < terrain * 0.5f)
            avg = terrain;
        var airColumn = planet.AtmosphereAltitude;
        if (airColumn < 200f)
            airColumn = Math.Max(hill - avg, 200f);
        airTop = avg + airColumn;
        visualCeil = airTop;
        if (visualCeil <= terrain + 80f)
            visualCeil = terrain + 200f;
    }

    static void BuildLayerBands(List<float> rels, out Vector4 peaks, out Vector4 widths)
    {
        peaks = Vector4.Zero;
        widths = Vector4.Zero;
        int n = rels == null ? 0 : rels.Count;
        if (n <= 0)
        {
            peaks.X = 0.42f;
            widths.X = 0.55f;
            return;
        }

        var sorted = rels.ToArray();
        Array.Sort(sorted);
        float rMin = sorted[0];
        float rMax = sorted[n - 1];
        float span = rMax - rMin;
        for (int i = 0; i < n; i++)
        {
            float p = span < 1e-4f
                ? (n == 1 ? 0.42f : i / (float)(n - 1))
                : (sorted[i] - rMin) / span;
            float prev = i > 0
                ? (span < 1e-4f ? (i - 1) / (float)(n - 1) : (sorted[i - 1] - rMin) / span)
                : p;
            float next = i < n - 1
                ? (span < 1e-4f ? (i + 1) / (float)(n - 1) : (sorted[i + 1] - rMin) / span)
                : p;
            float gap = Math.Max(p - prev, next - p);
            float w = Math.Max(0.22f, 0.55f * gap + 0.12f);
            switch (i)
            {
                case 0:
                    peaks.X = p;
                    widths.X = w;
                    break;
                case 1:
                    peaks.Y = p;
                    widths.Y = w;
                    break;
                case 2:
                    peaks.Z = p;
                    widths.Z = w;
                    break;
                default:
                    peaks.W = p;
                    widths.W = w;
                    break;
            }
        }
    }
}
