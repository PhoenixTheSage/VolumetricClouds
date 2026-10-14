using System;
using System.Globalization;
using System.Text;
using ClientPlugin.Anomaly;
using ClientPlugin.Gui;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Clouds;

/// <summary>
/// Live diagnostics for shell radii, packed uniforms, textures, and Anomaly
/// program status.
/// </summary>
public static class CloudsStatus
{
    public static string CurrentText
    {
        get
        {
            var sb = new StringBuilder();
            AppendConfig(sb);
            sb.AppendLine();
            AppendPlanet(sb);
            sb.AppendLine();
            AppendPacked(sb);
            sb.AppendLine();
            AppendTextures(sb);
            sb.AppendLine();
            AppendAnomaly(sb);
            return sb.ToString();
        }
    }

    public static void Show()
    {
        MyGuiSandbox.AddScreen(new StatusScreen(CurrentText));
    }

    public static string HudOverlayLine
    {
        get
        {
            if (Config.Current.Enabled && Config.Current.VolumeStudio)
            {
                var mode = Config.Current.DebugView;
                return mode == CloudDebugView.Off
                    ? "volume studio"
                    : "volume studio  " + mode;
            }

            var line = CloudSampler.HudLine;
            var view = Config.Current.DebugView;
            if (view == CloudDebugView.Off)
                return line;
            if (string.IsNullOrEmpty(line))
                return "debug " + view;
            return line + "  debug " + view;
        }
    }

    static void AppendConfig(StringBuilder sb)
    {
        var cfg = Config.Current;
        sb.AppendLine("Config");
        sb.Append("  ").Append(cfg.Enabled ? "on" : "off");
        sb.Append(" · studio ").Append(cfg.VolumeStudio ? "on" : "off");
        sb.Append(" · previews ").Append(StudioPlacer.PlacedCount);
        sb.Append(" · planetary march off");
        sb.Append(" · replace ").Append(Yes(cfg.ReplaceVanilla));
        sb.Append(" · ").Append(cfg.Quality);
        sb.Append(" · steps ").Append(cfg.StepCount);
        sb.Append(" / light ").AppendLine(cfg.LightStepCount.ToString(CultureInfo.InvariantCulture));
        sb.Append("  amount ").Append(F(cfg.Coverage));
        sb.Append(" · spacing ").Append(F(cfg.Spacing));
        sb.Append(" · density ").Append(F(cfg.Density));
        sb.Append(" · wind ").AppendLine(F(cfg.WindSpeed));
        sb.Append("  min/max ").Append(F(cfg.BaseAltitude));
        sb.Append(" / ").Append(F(cfg.MaxAltitude));
        sb.Append(" · atmo fit ").Append(F(cfg.AtmosphereFit));
        sb.Append(" · convective/cirrus ").Append(F(cfg.LowCoverage));
        sb.Append(" / ").AppendLine(F(cfg.CirrusStrength));
        sb.Append("  horizon ").Append(F(cfg.HorizonRange));
        sb.Append(" · HDR lift ").AppendLine(F(cfg.HdrLift));
        sb.Append("  shape/detail ").Append(F(cfg.ShapeScale));
        sb.Append(" / ").Append(F(cfg.DetailScale));
        sb.Append(" · erosion ").Append(F(cfg.ErosionStrength));
        sb.Append(" · fine step ").AppendLine(M(cfg.SampleSpacing));
        sb.Append("  debug ").AppendLine(cfg.DebugView.ToString());
        if (cfg.DebugView != CloudDebugView.Off)
            sb.Append("  ").AppendLine(DebugLegend(cfg.DebugView));
    }

    static void AppendPlanet(StringBuilder sb)
    {
        var d = CloudSampler.Diagnostics;
        sb.AppendLine("Planet");
        if (!d.Active)
        {
            sb.Append("  inactive: ").AppendLine(Empty(d.InactiveReason));
            sb.Append("  HUD: ").AppendLine(Empty(CloudSampler.HudLine));
            return;
        }

        sb.Append("  ").Append(Empty(d.PlanetName));
        sb.Append(" · cam ").Append(Km(d.CameraDistanceMeters));
        sb.Append(" · layers ").AppendLine(d.CloudLayerCount.ToString(CultureInfo.InvariantCulture));
        sb.Append("  terrain ").Append(Km(d.TerrainRadius));
        sb.Append(" · hills ").Append(Km(d.HillRadius));
        sb.Append(" · sea ").AppendLine(Km(d.SeaRadius > 1f ? d.SeaRadius : d.AverageRadius));
        sb.Append("  Keen atmo ").Append(Km(d.AtmosphereRadius));
        sb.Append(" · airTop ").Append(Km(d.AirTop));
        sb.Append(" · visualCeil ").AppendLine(Km(d.VisualCeil));
        sb.Append("  ceil ").AppendLine(d.CeilFromAnomaly ? "Anomaly" : "local fallback");
        sb.Append("  shell ").Append(Km(d.InnerRadius));
        sb.Append(" / ").Append(Km(d.OuterRadius));
        sb.Append(" · span ").AppendLine(Km(d.OuterRadius - d.InnerRadius));
        sb.Append("  min01/max01 ").Append(F(d.Min01));
        sb.Append(" / ").Append(F(d.Max01));
        sb.Append(" · terrainHug ").Append(Yes(d.TerrainHug > 0.5f));
        sb.Append(" · weather ").AppendLine(Empty(d.WeatherTexture));
        sb.Append("  HUD: ").AppendLine(Empty(CloudSampler.HudLine));
    }

    static void AppendPacked(StringBuilder sb)
    {
        var p = CloudRenderer.LastPack;
        sb.AppendLine("Uniforms");
        if (!p.Valid)
        {
            sb.AppendLine("  (none yet)");
            return;
        }

        sb.Append("  fade ").Append(F(p.Fade));
        sb.Append(" · camToShell ").Append(Km(p.CamToShell));
        sb.Append(" · viewLod ").Append(F(CloudRenderer.ViewLod(p.CamToShell)));
        sb.Append(" · amount ").Append(F(p.Coverage));
        sb.Append(" · spacing ").Append(F(p.Spacing));
        sb.Append(" · density ").AppendLine(F(p.Density));
        sb.Append("  volumeSize ").Append(M(p.VolumeSize));
        sb.Append(" · hills ").Append(Km(p.PackedHills));
        sb.Append(" · airTop ").AppendLine(Km(p.PackedAirTop));
        sb.Append("  budget ").Append(F(p.StepBudget));
        sb.Append(" / light ").Append(F(p.LightStepBudget));
        sb.Append(" · fine step ").Append(M(p.SampleSpacing));
        sb.Append(" · shape/detail/erosion ").Append(F(p.ShapeScale));
        sb.Append('/').Append(F(p.DetailScale));
        sb.Append('/').AppendLine(F(p.ErosionStrength));
        sb.Append("  climate wrap ").Append(M(p.WeatherScale));
        sb.Append(" · sim ").Append(CloudClimate.Running ? CloudClimate.Steps + " steps" : "idle");
        if (CloudClimate.Running)
            sb.Append(" · ").Append(CloudClimate.MoistureLine);
        sb.Append(" · terrain ").Append(Km(p.PackedTerrain));
        sb.Append(" · convective/cirrus ").Append(F(p.Low));
        sb.Append(" / ").AppendLine(F(p.High));
        sb.Append("  atmo fit ").Append(F(p.AtmosphereFit));
        sb.Append(" · inner/outer ").Append(Km(p.PackedInner));
        sb.Append(" / ").AppendLine(Km(p.PackedOuter));
        sb.Append("  debug ").Append(((CloudDebugView)Math.Max(p.DebugView, 0)).ToString());
        sb.Append(" · shared near ").Append(Km(SharedMediumAdapter.NearDistance));
        sb.Append(" · HDR tenant ").AppendLine(Yes(p.HasDisplayTenant));
    }

    static void AppendTextures(StringBuilder sb)
    {
        sb.AppendLine("Textures");
        sb.Append("  ").AppendLine(CloudTextures.StatusLine());
    }

    static void AppendAnomaly(StringBuilder sb)
    {
        sb.AppendLine("Anomaly");
        sb.Append("  registered ").Append(Yes(AnomalyBridge.IsRegistered));
        sb.Append(" · registry ").AppendLine(Yes(AnomalyBridge.RegistryFound));
        AppendProgram(sb, "shadow", AnomalyBridge.ShadowPassId);
        AppendProgram(sb, "occlude", AnomalyBridge.OccludePassId);
        AppendProgram(sb, "volume", AnomalyBridge.PassId);
        AppendProgram(sb, "studio", AnomalyBridge.StudioPassId);
        sb.Append("  catalog ");
        sb.Append(AnomalyBridge.ShapeName).Append(' ').Append(Yes(AnomalyBridge.CatalogAvailable(AnomalyBridge.ShapeName)));
        sb.Append(" · ").Append(AnomalyBridge.DetailName).Append(' ').Append(Yes(AnomalyBridge.CatalogAvailable(AnomalyBridge.DetailName)));
        sb.Append(" · ").Append(AnomalyBridge.WeatherName).Append(' ').AppendLine(Yes(AnomalyBridge.CatalogAvailable(AnomalyBridge.WeatherName)));
        sb.Append("  pack ").AppendLine(Empty(AnomalyBridge.PackRoot));
    }

    static void AppendProgram(StringBuilder sb, string label, string id)
    {
        if (!AnomalyBridge.ProgramStatusAvailable)
        {
            sb.Append("  ").Append(label).AppendLine("  unknown");
            return;
        }

        if (AnomalyBridge.TryGetProgramStatus(id, out var status))
            sb.Append("  ").Append(label).Append("  ").AppendLine(status);
        else
            sb.Append("  ").Append(label).Append("  missing id=").AppendLine(id);
    }

    static string DebugLegend(CloudDebugView view)
    {
        switch (view)
        {
            case CloudDebugView.WeatherCoverage: return "live lon/lat climate  ramp=cloud";
            case CloudDebugView.WeatherType: return "live  R=cirrus G=base B=thick";
            case CloudDebugView.WeatherUv: return "shell  longitude wraps  G=latitude  yellow=64² texel";
            case CloudDebugView.WeatherMap: return "top-right 64² RGB  rest=shell coverage";
            case CloudDebugView.Altitude: return "shell  ramp=altitude 01";
            case CloudDebugView.HeightProfile: return "shell  ramp=weather×height potential";
            case CloudDebugView.CoverageAmount: return "shell  ramp=Amount after mask";
            case CloudDebugView.ShapeNoise: return "shell  ramp=Perlin-Worley remap(R, Worley FBM)";
            case CloudDebugView.DensityBase: return "shell  ramp=Evolved/HZD density";
            case CloudDebugView.DensityEroded: return "shell  ramp=after Cubed erosion";
            case CloudDebugView.StormBody: return "march  ramp=analytic anvil and funnel";
            case CloudDebugView.MarchSteps: return "ramp=steps/budget";
            case CloudDebugView.MarchCoarseFine: return "R=coarse  G=fine";
            case CloudDebugView.MarchEmpty: return "ramp=fine-empty exits";
            case CloudDebugView.Transmittance: return "ramp=1−T  (optical depth)";
            case CloudDebugView.SamplePos: return "RGB=frac(east,alt,north)/shapePeriod";
            case CloudDebugView.ViewLod: return "ramp=CloudViewLod";
            default: return "";
        }
    }

    static string Yes(bool value) => value ? "yes" : "no";
    static string Empty(string value) => string.IsNullOrEmpty(value) ? "(none)" : value;
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Km(float meters) => (meters / 1000f).ToString("0.##", CultureInfo.InvariantCulture) + " km";
    static string M(float meters) => meters.ToString("0.#", CultureInfo.InvariantCulture) + " m";
}
