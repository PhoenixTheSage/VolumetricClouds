using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Clouds;
using ClientPlugin.Settings;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.RichHud;

internal static class AnomalyTerminalHook
{
    public const string RegistryTypeName = "ClientPlugin.RichHud.TerminalConfigRegistry";
    public const string FolderTitle = "Volumetric Clouds";
    public const string SettingsPage = "Settings";
    public const string StatusPage = "Status";

    static readonly object Gate = new();
    static bool installed;
    static bool overlayInstalled;
    static object pageInstance;
    static Type pageType;
    static string lastHud;

    public static bool TryInstall()
    {
        lock (Gate)
        {
            TryInstallOverlay();
            if (installed)
                return true;

            var page = RequestFolder(SettingsPage, FolderTitle);
            if (page == null)
                return false;

            Populate(page);
            var studio = RequestNested(SettingsPage, FolderTitle, "Volume Studio");
            if (studio != null)
                PopulateStudio(studio);
            var genus = RequestNested("Genus", FolderTitle, "Volume Studio");
            if (genus != null)
                PopulateStudioGenus(genus);
            var funnel = RequestNested("Funnel", FolderTitle, "Volume Studio");
            if (funnel != null)
                PopulateStudioFunnel(funnel);
            var status = RequestFolder(StatusPage, FolderTitle);
            if (status != null)
                PopulateStatus(status);
            installed = true;
            MyLog.Default.WriteLine($"{Plugin.Name}: Rich HUD pages under Anomaly Shaders / {FolderTitle}");
            return true;
        }
    }

    public static void TryRefresh()
    {
        object page;
        Type t;
        lock (Gate)
        {
            page = pageInstance;
            t = pageType;
        }

        if (page == null || t == null)
            return;

        var line = CloudsStatus.HudOverlayLine ?? "No planet in range yet";
        if (line == lastHud)
            return;
        Invoke(t, page, "Refresh");
        lastHud = line;
    }

    static object RequestFolder(string pageTitle, string folderTitle)
    {
        return RequestFolderCore(pageTitle, new[] { folderTitle });
    }

    static object RequestNested(string pageTitle, params string[] folders)
    {
        return RequestFolderCore(pageTitle, folders);
    }

    static object RequestFolderCore(string pageTitle, string[] folders)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == null || assembly == typeof(AnomalyTerminalHook).Assembly)
                continue;

            Type registry;
            try
            {
                registry = assembly.GetType(RegistryTypeName, false, false);
            }
            catch
            {
                continue;
            }

            if (registry == null)
                continue;

            try
            {
                var nested = registry.GetMethod("RequestFolderPage", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(IReadOnlyList<string>) }, null);
                if (nested != null)
                    return nested.Invoke(null, new object[] { pageTitle, folders });

                if (folders.Length == 1)
                {
                    var two = registry.GetMethod("RequestFolderPage", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string), typeof(string) }, null);
                    return two?.Invoke(null, new object[] { folders[0], pageTitle });
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Warning($"{Plugin.Name}: RequestFolderPage failed: {e.Message}");
                return null;
            }
        }

        return null;
    }

    static void TryInstallOverlay()
    {
        if (overlayInstalled)
            return;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == null || assembly == typeof(AnomalyTerminalHook).Assembly)
                continue;

            Type registry;
            try
            {
                registry = assembly.GetType("ClientPlugin.RichHud.HudOverlayRegistry", false, false);
            }
            catch
            {
                continue;
            }

            if (registry == null)
                continue;

            var register = registry.GetMethod("Register", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(Func<string>) }, null);
            if (register == null)
                continue;

            try
            {
                register.Invoke(null, new object[]
                {
                    "volumetric.clouds",
                    (Func<string>)(() => CloudsStatus.HudOverlayLine)
                });
                overlayInstalled = true;
                return;
            }
            catch (Exception e)
            {
                MyLog.Default.Warning($"{Plugin.Name}: HudOverlay Register failed: {e.Message}");
                return;
            }
        }
    }

    static void Populate(object page)
    {
        var t = page.GetType();
        pageInstance = page;
        pageType = t;
        lastHud = null;
        Invoke(t, page, "Category", "Volumetric Clouds");
        Invoke(t, page, "Checkbox", "Enabled",
            (Func<bool>)(() => Config.Current.Enabled),
            (Action<bool>)(v => Set(() => Config.Current.Enabled = v)),
            "Master switch for raymarched clouds");
        Invoke(t, page, "Checkbox", "Replace vanilla layers",
            (Func<bool>)(() => Config.Current.ReplaceVanilla),
            (Action<bool>)(v => Set(() => Config.Current.ReplaceVanilla = v)),
            "Hide Keen CloudSphere layers while this pack is drawing");
        Invoke(t, page, "Dropdown", "Quality", typeof(CloudQuality),
            (Func<object>)(() => Config.Current.Quality),
            (Action<object>)(v => Set(() => Config.Current.Quality = (CloudQuality)v)),
            "Adaptive raymarch ceiling. Medium follows Nubis' 54–96 sample range.");
        Invoke(t, page, "Slider", "Amount", 0.05f, 2f,
            (Func<float>)(() => Config.Current.Coverage),
            (Action<float>)(v => Set(() => Config.Current.Coverage = v)),
            "How filled each cloud is. Does not change how far apart they sit.", 0.01f);
        Invoke(t, page, "Slider", "Spacing", 0.35f, 3f,
            (Func<float>)(() => Config.Current.Spacing),
            (Action<float>)(v => Set(() => Config.Current.Spacing = v)),
            "Storm-track breakup on the global climate map. 1 = a few systems around the planet. Does not tile coverage.", 0.05f);
        Invoke(t, page, "Slider", "Density", 0.05f, 1.2f,
            (Func<float>)(() => Config.Current.Density),
            (Action<float>)(v => Set(() => Config.Current.Density = v)),
            "Cloud optical density", 0.01f);

        Invoke(t, page, "Category", "Density model",
            "Perlin-Worley base shape, Worley edge erosion, and adaptive sampling.");
        Invoke(t, page, "Slider", "Base shape size", 0.5f, 3f,
            (Func<float>)(() => Config.Current.ShapeScale),
            (Action<float>)(v => Set(() => Config.Current.ShapeScale = v)),
            "Physical scale of the 128³ Perlin-Worley billows. 1 = researched default.", 0.05f);
        Invoke(t, page, "Slider", "Detail size", 0.25f, 4f,
            (Func<float>)(() => Config.Current.DetailScale),
            (Action<float>)(v => Set(() => Config.Current.DetailScale = v)),
            "Physical scale of the 64³ Worley erosion. Higher = larger, softer detail.", 0.05f);
        Invoke(t, page, "Slider", "Erosion", 0f, 2f,
            (Func<float>)(() => Config.Current.ErosionStrength),
            (Action<float>)(v => Set(() => Config.Current.ErosionStrength = v)),
            "Worley edge erosion strength. 0 disables fine erosion; 1 = researched default.", 0.02f);
        Invoke(t, page, "Slider", "Sample spacing", 20f, 120f,
            (Func<float>)(() => Config.Current.SampleSpacing),
            (Action<float>)(v => Set(() => Config.Current.SampleSpacing = v)),
            "Minimum fine ray step in metres. Quality still caps total samples.", 2f);

        Invoke(t, page, "Category", "Height",
            "Min/Max are clamps only. Height comes from the weather column.");
        Invoke(t, page, "Label", "Column",
            (Func<string>)(() => CloudsStatus.HudOverlayLine ?? "No planet in range yet"));
        Invoke(t, page, "Slider", "Min height", 0f, 5f,
            (Func<float>)(() => Config.Current.BaseAltitude),
            (Action<float>)(v => Set(() => Config.Current.BaseAltitude = v)),
            "Same unit as Max. 0 = sea. 1 = the fitted limb. Hides decks below the floor.", 0.01f);
        Invoke(t, page, "Slider", "Max height", 0f, 5f,
            (Func<float>)(() => Config.Current.MaxAltitude),
            (Action<float>)(v => Set(() => Config.Current.MaxAltitude = v)),
            "Same unit as Min. 1 = the fitted limb. Above 1 continues past it in the same metres.", 0.01f);
        Invoke(t, page, "Slider", "Atmosphere fit", 0.35f, 1f,
            (Func<float>)(() => Config.Current.AtmosphereFit),
            (Action<float>)(v => Set(() => Config.Current.AtmosphereFit = v)),
            "At Max 1, how far IsolatedMix reaches toward extras air top. Lower hugs the visible blue limb.", 0.01f);
        Invoke(t, page, "Slider", "Convective", 0f, 1.5f,
            (Func<float>)(() => Config.Current.LowCoverage),
            (Action<float>)(v => Set(() => Config.Current.LowCoverage = v)),
            "Convective field: stratus through cumulonimbus, plus alto", 0.01f);

        Invoke(t, page, "Category", "Appearance");
        Invoke(t, page, "Slider", "Wind speed", 0f, 8f,
            (Func<float>)(() => Config.Current.WindSpeed),
            (Action<float>)(v => Set(() => Config.Current.WindSpeed = v)),
            "Cloud drift. 1 wraps the weather map in about 15 minutes", 0.05f);
        Invoke(t, page, "Color", "Albedo tint",
            (Func<Color>)(() => Config.Current.AlbedoTint),
            (Action<Color>)(v => Set(() => Config.Current.AlbedoTint = v)),
            "Multiplies the planet CloudLayer albedo");
        Invoke(t, page, "Slider", "HDR lift", 1f, 16f,
            (Func<float>)(() => Config.Current.HdrLift),
            (Action<float>)(v => Set(() => Config.Current.HdrLift = v)),
            "Extra brightness when an HDR Display pack is live. 1 = same as SDR.", 0.5f);

        Invoke(t, page, "Category", "Horizon");
        Invoke(t, page, "Slider", "Horizon range", 0.5f, 8f,
            (Func<float>)(() => Config.Current.HorizonRange),
            (Action<float>)(v => Set(() => Config.Current.HorizonRange = v)),
            "Max view distance in atmosphere radii. Does not change Min/Max or Atmosphere fit", 0.1f);

        Invoke(t, page, "Category", "Debug");
        Invoke(t, page, "Dropdown", "View", typeof(CloudDebugView),
            (Func<object>)(() => Config.Current.DebugView),
            (Action<object>)(v => Set(() => Config.Current.DebugView = (CloudDebugView)v)),
            "False-color IsolatedMix. Weather / density / march. Off = shaded clouds.");
        Invoke(t, page, "Button", "Show Status",
            (Action)CloudsStatus.Show,
            "Shell radii, packed VolumeHdr, textures, Anomaly program/catalog. Prefer over the HUD overlay.");
    }

    static void Slider(object page, string name, float min, float max, float step, Func<float> get, Action<float> set, string tip)
    {
        Invoke(page.GetType(), page, "Slider", name, min, max, get, set, tip, step);
    }

    static void PopulateStudio(object page)
    {
        var t = page.GetType();
        Invoke(t, page, "Category", "Volume Studio",
            "One cumulus on a clear sky. Planetary volumes stay off.");
        Invoke(t, page, "Checkbox", "Enabled",
            (Func<bool>)(() => Config.Current.VolumeStudio),
            (Action<bool>)(v => Set(() => Config.Current.VolumeStudio = v)),
            "On replaces the view with the studio. Off shows Keen's atmosphere.");
        Invoke(t, page, "Dropdown", "Sky", typeof(StudioSky),
            (Func<object>)(() => Config.Current.StudioSkyMode),
            (Action<object>)(v => Set(() => Config.Current.StudioSkyMode = (StudioSky)v)),
            "Background. White is for judging gray values. Blue matches a day sky.");
        Invoke(t, page, "Dropdown", "View", typeof(CloudDebugView),
            (Func<object>)(() => Config.Current.DebugView),
            (Action<object>)(v => Set(() => Config.Current.DebugView = (CloudDebugView)v)),
            "Off is the lit cloud. Height, shape, and density views false-color this volume.");

        Invoke(t, page, "Category", "Volume",
            "Which preview is in the box. Shape controls live in the Genus and Funnel folders.");
        Invoke(t, page, "Dropdown", "Volume", typeof(StudioVolume),
            (Func<object>)(() => Config.Current.StudioVolumeMode),
            (Action<object>)(v => Set(() => Config.Current.StudioVolumeMode = (StudioVolume)v)),
            "Cloud is the genus alone. Funnel adds a vortex under that cloud.");

        Invoke(t, page, "Category", "Frame",
            "Where the test volume sits in front of the camera.");
        Slider(page, "Distance", 600f, 8000f, 50f, () => Config.Current.StudioDistance, v => Config.Current.StudioDistance = v, "Metres in front of the camera.");
        Slider(page, "Lift", -800f, 800f, 10f, () => Config.Current.StudioLift, v => Config.Current.StudioLift = v, "Metres up from the view center.");
        Slider(page, "Width", 200f, 4000f, 20f, () => Config.Current.StudioWidth, v => Config.Current.StudioWidth = v, "Half-extent left to right, metres.");
        Slider(page, "Height", 80f, 2000f, 10f, () => Config.Current.StudioHeight, v => Config.Current.StudioHeight = v, "Half-extent up, metres.");
        Slider(page, "Depth", 200f, 4000f, 20f, () => Config.Current.StudioDepth, v => Config.Current.StudioDepth = v, "Half-extent along the view, metres.");
        Invoke(t, page, "Button", "Place Preview",
            (Action)StudioPlacer.Begin,
            "Closes this terminal. The box follows the crosshair. Page Up/Down, Home/End, and Insert/Delete turn it. Left click places it in the world. Right click or Escape cancels.");
        Invoke(t, page, "Button", "Remove All",
            (Action)StudioPlacer.RemoveAll,
            "Deletes every preview volume placed in the world.");

        Invoke(t, page, "Category", "Orientation",
            "Turns the volume in front of the camera. Zero faces the view.");
        Slider(page, "Yaw", -180f, 180f, 1f, () => Config.Current.StudioYaw, v => Config.Current.StudioYaw = v, "Degrees around the view's up axis.");
        Slider(page, "Pitch", -180f, 180f, 1f, () => Config.Current.StudioPitch, v => Config.Current.StudioPitch = v, "Degrees around the view's right axis.");
        Slider(page, "Roll", -180f, 180f, 1f, () => Config.Current.StudioRoll, v => Config.Current.StudioRoll = v, "Degrees around the view direction.");

        Invoke(t, page, "Category", "Raymarch",
            "Primary steps and the short march toward the sun. Capped for the test GPU.");
        Slider(page, "Sample spacing", 12f, 120f, 1f, () => Config.Current.StudioSampleSpacing, v => Config.Current.StudioSampleSpacing = v, "Fine step in metres inside the profile.");
        Slider(page, "Max steps", 16f, 96f, 1f, () => Config.Current.StudioMaxSteps, v => Config.Current.StudioMaxSteps = (int)Math.Round(v), "Primary march cap. Empty air uses the search step.");
        Slider(page, "Light steps", 1f, 6f, 1f, () => Config.Current.StudioLightSteps, v => Config.Current.StudioLightSteps = (int)Math.Round(v), "Samples toward the sun. Horizon Zero Dawn used six.");
        Slider(page, "Jitter", 0f, 1f, 0.01f, () => Config.Current.StudioJitter, v => Config.Current.StudioJitter = v, "Randomizes the first step so bands do not align.");
        Slider(page, "Empty skip", 1f, 8f, 0.1f, () => Config.Current.StudioSearch, v => Config.Current.StudioSearch = v, "How many fine steps to jump where the profile is empty.");
        Slider(page, "Stop at", 0.005f, 0.2f, 0.005f, () => Config.Current.StudioTransmittance, v => Config.Current.StudioTransmittance = v, "Exit when transmittance falls below this.");

        Invoke(t, page, "Category", "Noise",
            "Up-rez of the profile. A mode change does not invent a new cloud genus.");
        Invoke(t, page, "Dropdown", "Signal", typeof(StudioNoiseMode),
            (Func<object>)(() => Config.Current.StudioNoise),
            (Action<object>)(v => Set(() => Config.Current.StudioNoise = (StudioNoiseMode)v)),
            "Perlin-Worley is the cumulus remap. Billow, Worley, and inverted Worley are the same tile.");
        Slider(page, "Detail type", 0f, 1f, 0.01f, () => Config.Current.StudioDetailType, v => Config.Current.StudioDetailType = v, "0 wisps at the edge, 1 rounder billows. Blended only in Perlin-Worley.");
        Slider(page, "Shape size", 0.2f, 6f, 0.05f, () => Config.Current.StudioShapeScale, v => Config.Current.StudioShapeScale = v, "Period of the 128³ tile. 1 is about 720 m.");
        Slider(page, "Detail size", 0.1f, 8f, 0.05f, () => Config.Current.StudioDetailScale, v => Config.Current.StudioDetailScale = v, "Period of the 64³ erosion tile. Lower is finer wisps.");
        Slider(page, "Erosion", 0f, 4f, 0.02f, () => Config.Current.StudioErosion, v => Config.Current.StudioErosion = v, "Edge wisps and high-frequency carve. 0 is the smooth body. Above 1 replaces the rim with the wisp composite.");
        Slider(page, "Density", 0.05f, 2f, 0.01f, () => Config.Current.StudioDensity, v => Config.Current.StudioDensity = v, "Optical scale after erosion.");
        Slider(page, "Sharpen", 0.4f, 2f, 0.02f, () => Config.Current.StudioDensityPower, v => Config.Current.StudioDensityPower = v, "Power on density. Above 1 thins the haze and keeps the core.");
        Slider(page, "Wind", 0f, 8f, 0.05f, () => Config.Current.StudioWind, v => Config.Current.StudioWind = v, "Scrolls the noise. The profile stays put. 0 freezes it.");

        Invoke(t, page, "Category", "Light",
            "Beer-Lambert, dual-lobe Henyey-Greenstein, powder, and a cheap multiscatter sum.");
        Slider(page, "Extinction", 0.002f, 0.08f, 0.001f, () => Config.Current.StudioExtinction, v => Config.Current.StudioExtinction = v, "Beer coefficient. Higher darkens the far side.");
        Slider(page, "Shadow cap", 0.4f, 4f, 0.05f, () => Config.Current.StudioBeerCap, v => Config.Current.StudioBeerCap = v, "Maximum optical depth before the sun term floors.");
        Slider(page, "Forward g", 0f, 0.95f, 0.01f, () => Config.Current.StudioPhaseG, v => Config.Current.StudioPhaseG = v, "Henyey-Greenstein toward the sun. Higher is a brighter silver edge.");
        Slider(page, "Back g", -0.6f, 0.2f, 0.01f, () => Config.Current.StudioPhaseGBack, v => Config.Current.StudioPhaseGBack = v, "Second lobe. Negative scatters back toward the camera.");
        Slider(page, "Lobe mix", 0f, 1f, 0.01f, () => Config.Current.StudioPhaseMix, v => Config.Current.StudioPhaseMix = v, "0 is the sun that reached the sample. 1 adds the view-aligned glow on top of that.");
        Slider(page, "Powder", 0f, 1f, 0.01f, () => Config.Current.StudioPowder, v => Config.Current.StudioPowder = v, "Schneider sugar term. Darkens thick cloud and leaves the rim.");
        Slider(page, "Multiscatter", 0f, 1f, 0.01f, () => Config.Current.StudioMultiScatter, v => Config.Current.StudioMultiScatter = v, "Blends three cheaper scatter octaves into the sun term.");
        Slider(page, "Silver", 0f, 1.5f, 0.01f, () => Config.Current.StudioSilver, v => Config.Current.StudioSilver = v, "Extra light on the rim when you look toward the sun.");
        Slider(page, "Sun", 0f, 4f, 0.05f, () => Config.Current.StudioSunEnergy, v => Config.Current.StudioSunEnergy = v, "Scales the scene sun on the cloud.");
        Slider(page, "Ambient", 0f, 4f, 0.05f, () => Config.Current.StudioAmbient, v => Config.Current.StudioAmbient = v, "Scales in-cloud sky fill.");
        Slider(page, "Occlusion", 0f, 1f, 0.01f, () => Config.Current.StudioAo, v => Config.Current.StudioAo = v, "How hard the core blocks ambient. 0 is flat fill.");
        Slider(page, "Occlusion power", 0.2f, 2f, 0.02f, () => Config.Current.StudioAoPower, v => Config.Current.StudioAoPower = v, "Skin gradient. 0.5 is the Nubis ambient exponent.");
        Slider(page, "Base shade", 0f, 1f, 0.01f, () => Config.Current.StudioHeightAo, v => Config.Current.StudioHeightAo = v, "Darkens the underside. 0 leaves the base as bright as the crown.");
    }

    static void PopulateStudioGenus(object page)
    {
        var t = page.GetType();
        Invoke(t, page, "Category", "Genera",
            "Presets of one profile. Blend moves every shape float. The noise tile stays put.");
        Invoke(t, page, "Dropdown", "From", typeof(StudioGenus),
            (Func<object>)(() => Config.Current.StudioGenusFrom),
            (Action<object>)(v => Set(() => Config.Current.StudioGenusFrom = (StudioGenus)v)),
            "Genus at blend 0. Same order as the planetary types.");
        Invoke(t, page, "Dropdown", "Toward", typeof(StudioGenus),
            (Func<object>)(() => Config.Current.StudioMorphTarget),
            (Action<object>)(v => Set(() => Config.Current.StudioMorphTarget = (StudioGenus)v)),
            "Genus at blend 1.");
        Slider(page, "Blend", 0f, 1f, 0.01f, () => Config.Current.StudioMorph, v => Config.Current.StudioMorph = v, "0 is From. 1 is Toward. Writes the lerped profile into the sliders below.");

        Invoke(t, page, "Category", "Profile",
            "The dimensional profile is the cloud. Noise only carves it.");
        Slider(page, "Coverage", 0.05f, 2f, 0.01f, () => Config.Current.StudioCoverage, v => Config.Current.StudioCoverage = v, "Scales the profile. Low values shrink the mass.");
        Slider(page, "Base", 0.02f, 0.5f, 0.01f, () => Config.Current.StudioBaseSoft, v => Config.Current.StudioBaseSoft = v, "Nubis bottom gradient. Higher is a softer base.");
        Slider(page, "Crown", 0.2f, 0.98f, 0.01f, () => Config.Current.StudioCrown, v => Config.Current.StudioCrown = v, "Where the top starts to fall off.");
        Slider(page, "Envelope", 0.15f, 1.6f, 0.01f, () => Config.Current.StudioEnvelope, v => Config.Current.StudioEnvelope = v, "Horizontal width of the profile inside the box.");
        Slider(page, "Fill", 0f, 1f, 0.01f, () => Config.Current.StudioFill, v => Config.Current.StudioFill = v, "0 is a hump. 1 fills the window like a sheet.");
        Slider(page, "Lid", 0f, 1f, 0.01f, () => Config.Current.StudioLid, v => Config.Current.StudioLid = v, "Flattens the top. High is an inversion cap.");
        Slider(page, "Anvil", 0f, 1f, 0.01f, () => Config.Current.StudioAnvil, v => Config.Current.StudioAnvil = v, "Shelves the crown and keeps it wide. Cumulonimbus.");
        Slider(page, "Shear", -1.5f, 1.5f, 0.01f, () => Config.Current.StudioShear, v => Config.Current.StudioShear = v, "Slides the crown along the long axis. Also streaks cirrus.");
        Slider(page, "Aspect", 0.15f, 4f, 0.01f, () => Config.Current.StudioAspect, v => Config.Current.StudioAspect = v, "Length versus width. Above 1 is a streak.");
        Slider(page, "Heading", 0f, 6.28f, 0.01f, () => Config.Current.StudioHeading, v => Config.Current.StudioHeading = v, "Radians. Rotates the long axis and the shear.");
        Slider(page, "Breakup", 0f, 1f, 0.01f, () => Config.Current.StudioBreakup, v => Config.Current.StudioBreakup = v, "0 is one mass. 1 breaks the envelope into cells.");
        Slider(page, "Cells", 1f, 8f, 0.1f, () => Config.Current.StudioCells, v => Config.Current.StudioCells = v, "How many cells across the box when breakup is up.");
    }

    static void PopulateStudioFunnel(object page)
    {
        var t = page.GetType();
        Invoke(t, page, "Category", "Funnel",
            "The vortex under the genus cloud. The tip moves. The top stays in the cloud.");
        Invoke(t, page, "Dropdown", "Shape", typeof(StudioFunnelShape),
            (Func<object>)(() => Config.Current.StudioFunnelShapeMode),
            (Action<object>)(v => Set(() => Config.Current.StudioFunnelShapeMode = (StudioFunnelShape)v)),
            "Same radius curve as the planetary funnel. Rope, cone, stovepipe, wedge.");
        Invoke(t, page, "Dropdown", "Stage", typeof(StudioFunnelStage),
            (Func<object>)(() => Config.Current.StudioFunnelStageMode),
            (Action<object>)(v => Set(() => Config.Current.StudioFunnelStageMode = (StudioFunnelStage)v)),
            "Rope-out narrows the vortex and pulls the tip up. Dust at touchdown is not drawn here.");
        Slider(page, "Base radius", 20f, 800f, 5f, () => Config.Current.StudioFunnelRadius, v => Config.Current.StudioFunnelRadius = v, "Metres at the reference height of the radius curve.");
        Slider(page, "Funnel height", 0.2f, 1f, 0.01f, () => Config.Current.StudioFunnelHeight, v => Config.Current.StudioFunnelHeight = v, "How far the tip descends from the cloud. The top stays put.");
        Slider(page, "Wall", 0.05f, 1f, 0.01f, () => Config.Current.StudioFunnelWall, v => Config.Current.StudioFunnelWall = v, "1 is a solid tube. Lower punches a hollow core.");
        Slider(page, "Spin", 0f, 4f, 0.01f, () => Config.Current.StudioFunnelSpin, v => Config.Current.StudioFunnelSpin = v, "Radians per second. Rotates the wall and the subvortices.");
        Slider(page, "Wobble", 0f, 200f, 1f, () => Config.Current.StudioFunnelWobble, v => Config.Current.StudioFunnelWobble = v, "Metres the axis snakes as it spins.");
        Slider(page, "Intensity", 0f, 2f, 0.01f, () => Config.Current.StudioFunnelIntensity, v => Config.Current.StudioFunnelIntensity = v, "Optical scale of the tube.");
        Slider(page, "Subvortex X", -400f, 400f, 5f, () => Config.Current.StudioFunnelSubX, v => Config.Current.StudioFunnelSubX = v, "Metres, in the spinning frame.");
        Slider(page, "Subvortex Z", -400f, 400f, 5f, () => Config.Current.StudioFunnelSubZ, v => Config.Current.StudioFunnelSubZ = v, "Metres, in the spinning frame.");
    }

    static void PopulateStatus(object page)
    {
        var t = page.GetType();
        Invoke(t, page, "Category", "Debug");
        Invoke(t, page, "Dropdown", "View", typeof(CloudDebugView),
            (Func<object>)(() => Config.Current.DebugView),
            (Action<object>)(v => Set(() => Config.Current.DebugView = (CloudDebugView)v)),
            "False-color IsolatedMix. Weather / density / march. Off = shaded clouds.");
        Invoke(t, page, "Button", "Show Status",
            (Action)CloudsStatus.Show,
            "Shell radii, packed VolumeHdr, textures, Anomaly program/catalog. Prefer over the HUD overlay.");
    }

    static void Set(Action apply)
    {
        apply();
        ConfigStorage.Save(Config.Current);
    }

    static void Invoke(Type type, object instance, string name, params object[] args)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var method in type.GetMethods(flags))
        {
            if (method.Name != name || method.IsGenericMethodDefinition)
                continue;
            var parameters = method.GetParameters();
            if (args.Length > parameters.Length)
                continue;
            if (args.Length < parameters.Length)
            {
                var optional = true;
                for (var j = args.Length; j < parameters.Length; j++)
                {
                    if (!parameters[j].IsOptional && !parameters[j].HasDefaultValue)
                    {
                        optional = false;
                        break;
                    }
                }

                if (!optional)
                    continue;
            }

            var match = true;
            for (var i = 0; i < args.Length; i++)
            {
                var value = args[i];
                var expected = parameters[i].ParameterType;
                if (value == null)
                {
                    if (expected.IsValueType && Nullable.GetUnderlyingType(expected) == null)
                    {
                        match = false;
                        break;
                    }

                    continue;
                }

                if (!expected.IsInstanceOfType(value))
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;
            method.Invoke(instance, PadDefaults(method, args));
            return;
        }

        MyLog.Default.Warning($"{Plugin.Name}: Anomaly terminal missing {name}");
    }

    static object[] PadDefaults(MethodInfo method, object[] args)
    {
        var parameters = method.GetParameters();
        if (args.Length == parameters.Length)
            return args;
        var padded = new object[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            padded[i] = i < args.Length ? args[i] : parameters[i].DefaultValue;
        return padded;
    }
}
