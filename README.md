# Volumetric Clouds for Space Engineers

Replaces Keen’s textured planet CloudSphere layers with spherical, raymarched volumetric clouds.

This is an [Anomaly](https://github.com/PhoenixTheSage/Anomaly) shader pack. Enabling it auto-enables Anomaly.

For support please join the Pulsar Discord: https://discord.gg/z8ZczP2YZY

## Prerequisites

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/)
- [Pulsar](https://github.com/SpaceGT/Pulsar)
- [Anomaly Shader Framework](https://github.com/PhoenixTheSage/Anomaly) (pulled in as a dependency)

## How to use

Enable **Volumetric Clouds** in Pulsar's Plugins dialog. Anomaly is enabled automatically.

Configure the pack under **Anomaly Shaders → Volumetric Clouds → Settings** when
[Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081)
is in the world. The Pulsar plugin Settings dialog still works without Master.

Use **Show Status** (Settings / Status page, or Pulsar Settings) for shell radii,
packed VolumeHdr, textures, and Anomaly program/catalog truth. The HUD overlay is
a one-line summary and can lag or omit ceil-source / fade / texture state.

Settings write `{UserDataPath}/Storage/Clouds.cfg` after the sliders are quiet (~400 ms). They do not write on every mouse move.

## Functionality

**Temporary.** Planetary cloud drawing is off. Planets use Keen's atmosphere and, unless Volume studio is on, Keen's cloud spheres. Weather and storm topology still step on the planet. **Volume studio** (on by default) replaces the view with one cumulus, lit by the scene sun. Its toggle and the march, noise, occlusion, and scattering controls live under Anomaly Shaders → Volumetric Clouds → Volume Studio. Turn Enabled off there to see the stock planet. The shell march stays off until that studio model is signed off.

- AfterAtmosphere: PublishOnly `volumeSunShadow` stamp (GBuffer hits only, `scale` 1/2), IsolatedSub dest darken, then IsolatedMix volumes at **full-res**. The 64×64×1 weather map is a **coverage gate only** (Nubis dimensional profile) sampled in local east/north metres (`lon * R * cos(lat)`, `lat * R`) on ~16 km tiles (Spacing 1). Live density is the Nubis remap of one **128³ Perlin-Worley** tile (R billow, GBA Worley floor per type, 4 km wrap, Worley-4 ≈ 1 km puffs) plus 64³ Worley erosion at 100 m (`Pack/Noise/*_v5.bin`), sampled as azimuthal metres from the nearer pole. Do not use Cubed 100 m as the Evolved body (that packed 180 m foam into radial weather tubes). Mips follow sample distance. Coarse search skips empty air from the live weather×height profile; fine steps always sample full density + light + integrate. Procedural cone samples supply cloud self-shadowing. Horizon range is the in-atmosphere distance fade. **Min / max height** share one unit (0 = sea, 1 = the fitted limb) and feather type decks. GBuffer depth clips voxels/grids. Leaving the atmosphere is continuous `CloudViewLod` on the same IsolatedMix. Shared volumetrics owns its near interval; legacy IsolatedMix begins after that interval without overwriting sample spacing.
- Harmony skip of Keen `MyCloudRenderer.Render` while the pack is registered, enabled, and **Replace vanilla layers** is on. Turn that off (or disable the pack) to bring Keen spheres back. `MyAtmosphereRenderer` is not patched.
- Adaptive quality ceilings: Low 64 / Medium 96 / High 128 / Ultra 160 view iterations (light cone 4 / 5 / 6 / 6). Vertical rays use about 72% of the horizon ceiling and empty air uses coarse steps, matching the published Nubis 54–96 policy at Medium. Camera motion still feeds Anomaly `AnomalyMarchSteps`. Weather 64² is prepared on `MySession.AfterLoading`.
- Optional HDR lift when an HdrRender-class Display tenant is live.
- With SE-DLSS, some sky-through-cloud ghosting is expected in v1 (color in, no cloud motion vectors).

Anomaly Show Status should list AfterAtmosphere `Replace:volumetric.clouds.studio` while the studio is on. The planetary `PublishOnly:volumetric.clouds.shadow`, `IsolatedSub:volumetric.clouds.occlude`, and `IsolatedMix:volumetric.clouds.volume` programs stay disabled.

## Settings

| Control | Default | Notes |
|---------|---------|--------|
| Enabled | on | Master switch |
| Replace vanilla layers | on | Hide Keen CloudSphere draws |
| Quality | Medium | Raymarch / light-step budget |
| Amount / Spacing / Density | 1 / 0.7 / 1 | Fill inside weather cells, Nubis 2D NDF tile scale (1 = 16 km), optical density |
| Base shape / Detail size / Erosion | 1 / 1 / 1 | Evolved 128³ wrap (1 = 4 km, ~1 km puffs); Cubed 100 m Worley erosion; edge strength |
| Sample spacing | 24 m | Fine integration step (Nubis near). Coarse search covers empty air; Quality only caps iterations |
| Min height | 0.23 | Same unit as Max. 0 = sea. Hides type decks below the floor. Does not stretch them |
| Max height | 0.75 | Same unit as Min. 1 = the fitted limb. Above 1 continues past it in the same metres |
| Atmosphere fit | 0.35 | Where slider 1 sits, from hill tops toward extras air top. Lower hugs the visible blue limb (Anomaly VisualCeil still equals AirTop; Slice AP) |
| Wind speed | 1 | Weather-map drift (full wrap ~15 min at 1) plus planet layer spin |
| Convective | 1.25 | Weather-field amount. Type pages under Convective set Thickness (km-scale native depth) / Shape; height comes from weather G/B per column. Cirrus controls are hidden until that type is redesigned |
| Albedo tint | white | Multiplies the planet layer color |
| HDR lift | 4 | Only with a Display tenant; 1 = SDR |
| Horizon range | 2.5 | Max view distance in atmosphere radii. Does not change Min/Max height or Atmosphere fit. Leave-planet cull is automatic. |
| Show Status | button | Live shell / pack / texture / Anomaly dump |

## Rendering basis

The density and traversal model follows primary production references rather
than tuning a voxel texture until it resembles clouds:

- [Schneider & Vos, *The Real-time Volumetric Cloudscapes of Horizon Zero Dawn* (SIGGRAPH 2015)](https://www.advances.realtimerendering.com/s2015/The%20Real-time%20Volumetric%20Cloudscapes%20of%20Horizon%20-%20Zero%20Dawn%20-%20ARTR.pdf)
- [Schneider, *Nubis: Authoring Real-Time Volumetric Cloudscapes* (SIGGRAPH 2017)](https://advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf)
- [Schneider, *Nubis, Evolved* (SIGGRAPH 2022)](https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-NubisEvolved-NoVideos.pdf)
- [Schneider, *Nubis³* (SIGGRAPH 2023)](https://advances.realtimerendering.com/s2023/Nubis%20Cubed%20%28Advances%202023%29.pdf)
- [Hillaire, *Physically Based and Unified Volumetric Rendering in Frostbite*](https://www.ea.com/news/physically-based-unified-volumetric-rendering-in-frostbite)
- [Hillaire et al., `TileableVolumeNoise`](https://github.com/sebh/TileableVolumeNoise)

## Building

The plugin version lives in `Version.Build.props`. Folder overrides (`Bin64`, `Pulsar`) go in `Directory.Build.props.user` (run `setup.py` once).

Each successful build copies `plugin.dll`, `plugin.xml`, and the `Pack/` asset folder into Pulsar's `Local` plugin folder:

| Build     | Deployed to                                    |
|-----------|------------------------------------------------|
| `net48`   | `<Pulsar>/Legacy/Local/Clouds/`                |
| `net10.0` | `<Pulsar>/Interim/Local/Clouds/`               |
