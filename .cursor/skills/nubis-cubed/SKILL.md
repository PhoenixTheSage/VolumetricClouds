---
name: nubis-cubed
description: >-
  Applies Andrew Schneider's Nubis Cubed (SIGGRAPH 2023, Decima) cloud model:
  dimensional profile is the cloud, detail-type blends wispy and billowy
  composites, and the 128³ noise only up-rezzes that profile. Use when
  editing volumetric cloud density, cloud types, shape or detail noise,
  Worley, Perlin-Worley, Alligator noise, ray marches, or when the user
  mentions Nubis, Nubis Cubed, or Nubis3.
---

# Nubis Cubed

Source: Andrew Schneider, *Nubis3* (Nubis Cubed), Advances in Real-Time Rendering in Games, SIGGRAPH 2023. Local deck: `C:\Users\Zeridian\Desktop\Nubis Cubed.pdf`. Formulas and channel layout: [reference.md](reference.md).

## The cloud is the profile

Noise does not create cloud. A sample with dimensional profile 0 is empty. Do not sample the 3D detail texture there.

Build the profile first, then erode it:

```hlsl
cloud_density = saturate(noise_composite - (1.0 - dimensional_profile));
```

Coverage, min/max height, and type change the profile. They do not retune one noise tile into a different genus.

## Two models, same split

**2.5D (Nubis Evolved, reviewed in the deck).** Each model is 2D Nubis data fields over a ~16 km patch. Vertical-profile models (high cloud the player cannot reach): min height, max height, coverage, bottom type, top type. Envelope models (orographic, fly-through): min, max, type, density, and an edge gradient. The shader builds a low-resolution dimensional profile, then the 3D noise up-rezzes it. The profile recipe differs per model.

**Voxel (Nubis3).** The same three fields are stored, not rebuilt in the shader: dimensional profile, detail type, density scale (NVDF, on the order of 512×512×64, BC6, ~8 m voxels in the Burning Shores work). A separate signed-distance NVDF skips empty space. Fluid-sim "frankenclouds" are how those grids are authored. Do not invent a planet-sized dense voxel grid.

On this pack, a type may use an analytic profile or a small stored volume. It still has to be a profile. A frequency tweak, channel mix, or remap floor on one Worley tile is not a type.

## Detail noise is two families

The up-rez texture is one 128³, four channels, two families. It is not four octaves of one Worley, and it is not the silhouette.

| Channel | Nubis3 signal |
| --- | --- |
| R | Low-frequency Curly-Alligator (wisps) |
| G | High-frequency Curly-Alligator (wisps) |
| B | Low-frequency Alligator (billows) |
| A | High-frequency Alligator (billows) |

Schneider dropped Perlin-Worley and layered inverted Worley for this step because they read as packed spheres. Do not ship that as the cloud body.

Wisps blend low frequency in the dense core to high frequency at the edge, using the profile as the weight. Billows do the same with `pow(profile, 0.25)` so the edge stays round. Detail type then blends the two composites. See [reference.md](reference.md).

Close to the camera, fold the high-frequency channels (`abs` twice) and blend that in. Do not add a second 3D texture fetch for that.

Wind scrolls the noise sample position. It does not slide the profile off the terrain.

## Light and the march

Light energy is direct scattering plus ambient plus secondary. Ambient is `pow(1 - dimensional_profile, 0.5)`: light reaches the skin, not the core. Direct light is Beer-Lambert transmittance along one light march, times Henyey-Greenstein phase, plus a multiscatter term that is itself phased and reduced with depth and from below. Do not add a light march per secondary source. Model secondary energy (lightning) as a volume.

Step far in empty space (cone step plus distance step on the 2.5D height fields, or the SDF on voxel clouds). Fine steps are for occupied profile. Raise the noise mip with distance (`log2(1 + distance * scale)`); a fixed mip is wasted bandwidth. Cap the march. This pack's test GPU is an unstable RTX 2080 Ti.

## Before changing density

1. Name the dimensional profile for the type (altitude, thickness, edge, coverage). If the only change is a noise scale, stop.
2. Sample noise only where the profile is nonzero.
3. Build wispy and billowy composites from different signals, then `lerp` by the type at the sample.
4. Erode with the profile. Multiply density scale after erosion. Sharpen low-density regions with a power, do not punch holes through the core.
5. If a debug view paints the noise on the whole planet, it is showing the up-rez tile, not the cloud. The cloud view must go dark where the profile is empty.
