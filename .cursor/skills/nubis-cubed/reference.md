# Nubis Cubed reference

Andrew Schneider, *Nubis3*, Advances in Real-Time Rendering in Games, SIGGRAPH 2023 (Decima). The deck reviews Nubis Evolved (2.5D, 2022) and then the voxel up-rez used in Burning Shores. Slide text is paraphrased only where the OCR broke a sentence. Formulas are from the slides.

## 2.5D data fields

NDFs are 2D fields mapped over about 16 km. The shader decompresses a 3D cloud from them, then up-rezzes.

Vertical profile (high tropospheric cloud, not flown through):

```hlsl
// min, max, coverage, bottom type, top type
// top and bottom type index vertical-profile lookup textures
float dimensional_profile = vertical_profile * cloud_coverage;
```

Envelope (orographic, fly-through). Min and max bound the envelope. The profile is three gradients:

```hlsl
float height_fraction = remap(height, min_height, max_height, 0.0, 1.0);
float top_gradient = pow(1.0 - height_fraction, 1.5);
float bottom_gradient = pow(height_fraction, 2.0);
float edge_gradient = remap(sample_height, 0.0, 35.0, 1.0, 0.0);
float dimensional_profile = bottom_gradient * top_gradient * edge_gradient;
```

Erosion, both methods:

```hlsl
cloud_density = saturate(noise - (1.0 - dimensional_profile));
float3 noise_sample_pos = sample_pos - wind_direction * scroll_offset;
```

Earlier 128³ (Evolved), uncompressed 2 bytes/texel, 4.194 MB, four channels: low-frequency Perlin-Worley, low Worley, medium Worley, high Worley. Combined **per model** into a wispy composite and a billowy composite, then:

```hlsl
float noise_composite = lerp(wispy_noise, billowy_noise, detail_type);
```

Nubis3 says that layered inverted Worley needed heavy sampler work to stop looking like packed spheres, and Perlin-Worley was not wispy enough for the voxel up-rez. Do not treat that older layout as the Nubis3 detail texture.

## Voxel model

Density sampler order:

1. Sample modeling data (dimensional profile, detail type, density scale).
2. If `dimensional_profile` is 0, density is 0. Stop.
3. Else up-rez.

Modeling NVDF in the talk: 512×512×64, BC6, 1 byte/texel, 16.777 MB, voxel size called out at 8 m on the profile sample. SDF is a separate NVDF (same dimensions, original range −256 m to 4096 m, stored 0–1) so the march lookup does not drag the modeling channels along. BC4 on the SDF loses steps or artifacts; precision matters because the value is a distance.

Authoring path: Houdini Atlas tools. Sources include fluid sim (Aero), point clouds, meshes, and 2.5D fields, composited into frankenclouds. That pipeline is offline. The runtime contract is the NVDF fields, not the Houdini graph.

## Nubis3 detail texture

One 128³, four channels, uncompressed 2 bytes/texel in the slide (4.194 MB):

| Channel | Signal |
| --- | --- |
| R | Low-frequency Curly-Alligator (wisps). Inverted Alligator, then curl-distorted. |
| G | High-frequency Curly-Alligator |
| B | Low-frequency Alligator (billows) |
| A | High-frequency Alligator |

Alligator is Houdini's noise. SideFX publishes the HDK snippet: `https://www.sidefx.com/docs/hdk/alligator_2alligator_8_c-example.html`.

Sample:

```hlsl
inSamplePosition -= float3(cCloudWindOffset.x, cCloudWindOffset.y, 0.0) * voxel_cloud_animation_speed;
float mipmap_level = log2(1.0 + abs(inRaymarchInfo.mDistance * cVoxelFineDetailMipMapDistanceScale));
float4 noise = Cloud3DNoiseTextureC.SampleLOD(Cloud3DNoiseSamplerC, inSamplePosition * 0.01, mipmap_level);
```

`* 0.01` is the Cubed "GetUprezzed" frequency (100 m if position is metres). It is detail inside a cloud that already has a shape. It is not the cloud period.

### Composites

Wisps: dense core uses the low-frequency channel, the edge uses the high-frequency channel. The profile is already 0 at the skin and higher inside.

```hlsl
float wispy_noise = lerp(noise.r, noise.g, inDimensionalProfile);
```

Billows: edges need round structure, not high-frequency grit. `pow(profile, 0.25)` pulls the blend, and `* 0.3` keeps the billow signal from dominating the erosion.

```hlsl
float billowy_type_gradient = pow(inDimensionalProfile, 0.25);
float billowy_noise = lerp(noise.b * 0.3, noise.a * 0.3, billowy_type_gradient);
float noise_composite = lerp(wispy_noise, billowy_noise, detail_type);
```

The slide comment says "blend to wispy as the density scale decreases" on one line and uses `detail_type` as the weight on the next. The pictures are a blend from wispy to billowy over the type field. Keep type as that weight. Do not encode type as a UV scale.

### Close range, no second tap

Twice-folded noise. Expand to −1..1, fold over zero twice. Wisps use a high power, billows a lower one.

```hlsl
float hhf_wisps = 1.0 - pow(abs(abs(noise.g * 2.0 - 1.0) * 2.0 - 1.0), 4.0);
float hhf_billows = pow(abs(abs(noise.a * 2.0 - 1.0) * 2.0 - 1.0), 2.0);
```

Blend those with `detail_type` the same way, then blend from the base composite to this higher composite near the camera. The talk shows the handoff as seamless in flight.

### Apply

```hlsl
float uprezzed_density = ValueErosion(inDimensionalProfile, noise_composite);
uprezzed_density *= inDensityScale;
// Sharpen, more where density scale is low, so thin cloud keeps definition
// and undersampling noise drops.
uprezzed_density = pow(uprezzed_density, lerp(0.3, 0.6, max(EPSILON, pow(saturate(inDensityScale), 4.0))));
```

`ValueErosion` is the same relationship as the 2.5D line `saturate(noise - (1 - dimensional_profile))` (a remap of the profile by the noise). Noise cannot fill a hole the profile does not have.

Decreasing density in a real cloud is curly layered wisps. Increasing density is layered billows. The two families exist because of that, not as extra octaves.

## Light

```text
Light energy = direct + ambient + secondary
Direct = (transmittance * primary HG phase) + (multiple scattering * secondary HG phase)
```

Transmittance is Beer-Lambert from a march toward the sun. Phase is several Henyey-Greenstein lobes (forward-heavy, art-directed). Multiple scattering is light that arrives after more than one bounce; it is attenuated with optical depth and with height, because less light enters from below, then multiplied by its own phase.

```hlsl
float ambient_scattering = pow(1.0 - dimensional_profile, 0.5);
```

Ambient is the chance light from the sky and neighboring cloud reaches that point and then the eye. It is a skin gradient. No ambient ray march.

Secondary sources (lightning) are a modeled light volume sampled in the primary march. A per-sample light march for each bolt is a third march. Do not add one.

Full direct-light derivation is deferred by the deck to Schneider, *Nubis, Evolved* (SIGGRAPH 2022).

## March

Per pixel: step the ray, sample density, sample light, integrate, choose the next step, stop at full opacity.

Empty space is not a fine step. 2.5D flight uses cone step mapping (Dummer 2006) plus distance step mapping on the height fields. Voxel clouds write an SDF of the finished frankencloud and sphere-trace it (Hart 1995). Quilez for primitive distances, *Claybook* (Aaltonen) for high-frequency SDF use. Too-low SDF precision adds steps. Too-high compression artifacts the surface.

Integrate until opacity saturates. The 2.5D budget in the deck's history is about 0.2–5 ms depending on distance versus flying through.
