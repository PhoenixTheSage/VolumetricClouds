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

Settings write `{UserDataPath}/Storage/Clouds.cfg` after the sliders are quiet (~400 ms). They do not write on every mouse move.

## Functionality

- AfterAtmosphere IsolatedMix program (`volumetric.clouds`). Clouds occlude sky and sun.
- Nearest planet that has `CloudLayers` supplies shell altitude, tint, wind, and the weather map.
- Harmony skip of Keen `MyCloudRenderer.Render` while the pack is registered, enabled, and **Replace vanilla layers** is on. Turn that off (or disable the pack) to bring Keen spheres back.
- Quality presets: Low 16 / Medium 24 / High 32 / Ultra 48 view steps (light march 2 / 3 / 4 / 4). Camera motion drops steps via Anomaly `SafetyScale`.
- Optional HDR lift when an HdrRender-class Display tenant is live.
- With SE-DLSS, some sky-through-cloud ghosting is expected in v1 (color in, no cloud motion vectors).

Anomaly Show Status should list `Fullscreen: AfterAtmosphere/IsolatedMix:volumetric.clouds`.

## Settings

| Control | Default | Notes |
|---------|---------|--------|
| Enabled | on | Master switch |
| Replace vanilla layers | on | Hide Keen CloudSphere draws |
| Quality | High | Raymarch / light-step budget |
| Coverage / Density | 1.0 / 0.35 | Weather scale and optical density |
| Thickness | 0.04 | Shell thickness as a fraction of planet radius |
| Wind speed | 1 | Weather-map drift (full wrap ~15 min at 1) plus planet layer spin |
| Cirrus | 0.55 | High-altitude ice veil in the upper shell |
| Albedo tint | white | Multiplies the planet layer color |
| HDR lift | 4 | Only with a Display tenant; 1 = SDR |
| Fade start / end | 8 / 14 | Distance fade in atmosphere radii |

## Building

The plugin version lives in `Version.Build.props`. Folder overrides (`Bin64`, `Pulsar`) go in `Directory.Build.props.user` (run `setup.py` once).

Each successful build copies `plugin.dll`, `plugin.xml`, and the `Pack/` asset folder into Pulsar's `Local` plugin folder:

| Build     | Deployed to                                    |
|-----------|------------------------------------------------|
| `net48`   | `<Pulsar>/Legacy/Local/Clouds/`                |
| `net10.0` | `<Pulsar>/Interim/Local/Clouds/`               |
