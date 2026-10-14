# Storm roadmap

Path from the live 64×64 climate to pressure-driven storms and, later, tornado-scale detail. The target for the detail phases is the *shape* of ProtoManly's Weather for Minecraft: a few local storm objects, each with a life cycle, a raymarched body, and a funnel that is its own thing. That mod does not refine the whole world. Neither should this.

This document is the roadmap. Each phase has a prompt for a planning agent. Run one phase's prompt, accept that plan, implement it, then start the next. Do not implement a later phase inside an earlier plan.

## Scale

EarthLike hills sit near 67 km. The climate grid is 64×64, so one texel is about 6 km. A rope tornado is tens to a few hundred metres. A supercell is a few kilometres to a few tens. A hurricane is hundreds of kilometres and *can* live on this grid. A funnel cannot.

| Layer | What it owns | Resolution |
| --- | --- | --- |
| Climate grid | Moisture sources, highs, lows, where a storm may exist | ~6 km, 4 Hz, game thread |
| Storm object | Stage, world position, intensity, shape | metres, a handful, may tick every frame |
| Ray march | Shell clouds, plus an analytic body when a sample is near a storm | existing IsolatedMix step, ~40 m |
| Close effects | Rain, hail, debris, lightning | only when the camera is near |
| World damage | Voxels and grids the funnel touches | separate from the march, last |

ProtoManly's mod keeps a small number of storms near the player (on the order of a few hundred metres to a couple of kilometres), stages them from thunderstorm to supercell to mesocyclone to tornado, and raymarches the storm. Damage, sirens, and radar are gameplay on top of that object. Copy the object. Do not copy Minecraft block damage into the shader.

## Already shipped

[`CloudClimate.cs`](../ClientPlugin/Clouds/CloudClimate.cs) steps a 64×64 field at 4 Hz on the game thread. The render thread uploads it. The march is unchanged (fingerprint v86, `CloudDensity.hlsli` v72).

- `SourceFloor` is stationary. Water ice and windward slope feed it. Cold ice is weaker and does not get the tropical boost. Lee and deep night thin it. It does not advect.
- Eight `Storm` kernels ride `Zonal` / `Meridional`. Spacing scales radius. A wake decays toward the floor over a couple of minutes. Each kernel is max-stamped so the storm itself does not decay.
- `Reborn` scans the grid for the strongest daylit source. Status reports `storms N` beside water-ice and cold fractions.
- `WriteImage` still fills base height, thickness, and cirrus from the cell under the stamp. Elevation only lifts the deck.

The kernels slide on a latitude jet. They do not orbit, and they have no internal structure. That is the gap the phases close.

## Rules for every phase

- Game thread steps the climate. It does not touch D3D or write a `.cfg`. The render thread uploads.
- Keep `SimLock` then `PublishLock`. Do not invert them.
- No new voxel queries inside the 4 Hz step. Ice and snow were sampled once.
- Do not widen Anomaly's extras constant buffer past 320 bytes. Do not steal Keen slots, add a compile hook, or ship a Hidden plugin.
- Wind 0 freezes motion. The source floor stays put.
- Dual-TFM Debug build: `net10.0` and `net48`. Shader phases also run `Tests/ShaderCompile`.
- After code changes, `graphify update .` in this repo.
- The test GPU is an unstable RTX 2080 Ti. A `DEVICE_REMOVED` at Present is the wrapper. Do not add unbounded march loops.
- Do not edit this roadmap from a phase plan unless the user asked.

## Phase 1 — Pressure centers

**Depends on:** the shipped kernels.

**Outcome:** Highs and lows on the same 64×64 grid. Wind at a cell comes from the pressure slope plus a Coriolis turn, with the old zonal jet as a weak background. Lows are the eight storms. Highs are broader centers that do not stamp cloud. They suppress `SourceFloor` so a high is a clear patch. A low's stamp is a short spiral along its turning wind, not only an ellipse along the jet. Still no shader change. Weather Coverage should show vortices and clear eyes, not only translated blobs.

**Not in this phase:** hurricane eyes, funnels, particles, damage, a finer grid.

### Plan-agent prompt

```text
Plan only. Do not implement. Do not edit Docs/StormRoadmap.md.

Repo: T:\Cursor Projects\VolumetricClouds
Phase: Storm roadmap phase 1, pressure centers.

Read Docs/StormRoadmap.md (Phase 1 and Rules) and ClientPlugin/Clouds/CloudClimate.cs. The live sim is SourceFloor, eight Storm kernels, Zonal/Meridional advection, a decaying wake, and max-stamps in StampStorm. WriteImage fills G/B/A. The march and the 64×64 upload stay.

Design a plan that:
- Adds a few high-pressure centers and treats the eight storms as low-pressure centers. Each center has a texel position, a signed strength, and a radius. Pressure at a cell is the sum of those bumps. No Poisson solve. No new texture. No shader edit.
- Replaces pure Zonal/Meridional steering with a wind from the pressure gradient plus a Coriolis turn that flips across the equator. Keep Zonal as a weak background so belts still steer. Wind 0 still freezes motion.
- Makes highs suppress SourceFloor under them. Highs do not stamp cloud.
- Changes StampStorm so a low paints a short spiral along its local turning wind. The wake still advects on that wind and decays toward the floor. The storm stamp still uses max so the low does not decay away.
- Keeps rebirth on the strongest source (daylit water, windward slope, or cold ice in the polar and storm-track belts). A low deepens on MarineFeed/ColdFeed and still drains on lee and a long night. Belts only bias growth and lifetime.
- Updates MoistureLine so status can show highs as well as storms.
- Stays on the game thread at 4 Hz. No D3D, no cfg write, no new GetMaterialAt calls. Locks stay SimLock then PublishLock.

Out of scope: eyes, funnels, particles, voxel damage, refining the 64² grid, Anomaly extras growth.

Deliver an implementation plan with concrete edits in CloudClimate.cs, a test note (Weather Coverage and the map inset should show turning lows and clear highs), and todos. Dual-TFM Debug build. No shader compile.
```

## Phase 2 — Hurricanes on the grid

**Depends on:** phase 1.

**Outcome:** One kind of low can organize into a hurricane while it sits on water ice at low latitude, with enough turn to spin but without mid-latitude shear tearing it apart. The radius tightens, intensity goes to the cap, and the stamp cuts a calm eye with a bright ring. It weakens over cold ice, off the water, or under a strong high. This is still a CPU stamp into the 64×64 image. A hurricane is hundreds of kilometres, so the climate texel is enough.

**Not in this phase:** tornadoes, analytic march density, particles, damage.

### Plan-agent prompt

```text
Plan only. Do not implement. Do not edit Docs/StormRoadmap.md.

Repo: T:\Cursor Projects\VolumetricClouds
Phase: Storm roadmap phase 2, hurricanes on the 64² grid. Assume phase 1 pressure centers and gradient wind are in ClientPlugin/Clouds/CloudClimate.cs. If they are not, say so and stop.

Read Docs/StormRoadmap.md Phase 2 and the storm/pressure code in CloudClimate.cs.

Design a plan that:
- Adds an organized-cyclone state on a marine low. It may enter that state only over water ice (Ocean on the cell), in low latitudes where Coriolis is present but mid-latitude shear is weak, and only after it has been fed long enough to deepen.
- Tightens radius, raises intensity to the cap, and changes the stamp to a ring with a calm eye. The eye is a hole in the stamp, not a second texture.
- Weakens and leaves that state over cold ice, when Ocean feed drops, or when a high suppresses the cell. It must not pop off in one step.
- Keeps cold-ice storms thin and non-tropical. A cold kernel must not become a hurricane.
- Leaves the march, weather UV, and 4 Hz upload alone. WriteImage may use the cyclone state only to bias thickness and base height of cells under the ring.
- Extends MoistureLine with a short cyclone count or flag.

Out of scope: funnels, subvortices, particles, world damage, shader SDFs, extras CB growth.

Deliver an implementation plan with todos, the enter/exit rules in numbers (feed time, latitude band, shear limit), and how the eye is punched in StampStorm. Dual-TFM Debug build. No shader compile.
```

## Phase 3 — Storm objects in world metres

**Depends on:** phase 1. Phase 2 may land before or with this. The object does not require the hurricane stamp, but a cyclone flag on the parent low should be reserved.

**Outcome:** The 64×64 grid stops being asked to hold structure smaller than a texel. A handful of storm objects (on the order of the eight lows, not dozens) carry a world-space position in metres, a stage, an age, and a shape. Stages, in order: ordinary cell, supercell, mesocyclone, funnel aloft, tornado on the ground, rope-out. Not every low becomes a supercell. Promotion uses the coarse cell under the object: moisture feed, lift, shear against the background jet, and daylight. The climate grid still decides *where* an object may exist and still paints the synoptic blob. The object is the detail that later phases draw.

ProtoManly's storms spawn within a limited range and only a few exist. Here the objects follow the pressure lows, including ones on the far side of the planet. The expensive draw is gated in phase 4 by distance. This phase is CPU state only.

**Not in this phase:** shader edits, particles, damage.

### Plan-agent prompt

```text
Plan only. Do not implement. Do not edit Docs/StormRoadmap.md.

Repo: T:\Cursor Projects\VolumetricClouds
Phase: Storm roadmap phase 3, storm objects in world metres.

Read Docs/StormRoadmap.md Phase 3 and ClientPlugin/Clouds/CloudClimate.cs. Also see how a texel becomes a world position: CloudSampler builds the lon/lat grid from the planet sea radius, and CloudRenderer packs planet center. The climate texel is about 6 km on EarthLike. A tornado will never fit in that texel. This phase does not draw the tornado.

Design a plan that:
- Introduces a storm-object struct beside the climate Storm kernel, or replaces the kernel's draw-only fields with world metres, stage, age, and shape. Keep the count small and fixed (about eight).
- Stages: cell, supercell, mesocyclone, funnel aloft, tornado on the ground, rope-out. Promotion and demotion are explicit. A low over water ice or a windward slope, with shear against the background jet and enough feed, may become a supercell. A supercell may grow a mesocyclone. A mesocyclone may drop a funnel. Touchdown and rope-out are states, not a random stamp.
- Stores tornado shape as data only: rope, cone, stovepipe, or wedge, plus intensity and one or two subvortex offsets. Do not render them yet.
- Keeps the 64² image as the synoptic blob for that low. Rebirth and pressure motion still move the object. Map texel position to world metres with the planet center and sea radius already used by the climate grid. Do not query voxels each step.
- Lets the object integrate every frame if that is cheaper than the 4 Hz grid, but the grid step stays 4 Hz and still does not touch D3D.
- Adds a compact CPU snapshot the renderer can upload later (phase 4). Define the struct layout and the byte size. Do not bind it yet.
- Extends status with stage counts (how many supercells, how many tornadoes).

Out of scope: HLSL, pack SRVs, particles, debris, voxel or grid damage, Anomaly extras growth, Hidden plugins.

Deliver an implementation plan with the struct, the transition table, and todos. Dual-TFM Debug build. No shader compile.
```

## Phase 4 — Analytic storms in the march

**Depends on:** phase 3. The snapshot from that phase must exist.

**Outcome:** IsolatedMix samples an analytic density when the camera ray passes near a storm object. From tens of kilometres the supercell reads as an anvil and a hook. Inside about two kilometres the funnel, a ground-hugging debris skirt, and one or two subvortices appear. Beyond that, the storm remains the 64×64 blob. The funnel ends on the surface slice already in the weather volume (slice 1), so it meets hills instead of a smooth sphere. At most the nearest two storms are tested per sample. Orbit still looks like the synoptic map.

This is the first shader phase. Bump `CloudDensity.hlsli` and the fingerprint in `VolumetricClouds.hlsl`.

**Not in this phase:** rain particles, lightning, block damage.

### Plan-agent prompt

```text
Plan only. Do not implement. Do not edit Docs/StormRoadmap.md.

Repo: T:\Cursor Projects\VolumetricClouds
Phase: Storm roadmap phase 4, analytic storm density in the existing ray march.

Read Docs/StormRoadmap.md Phase 4, Pack/Fullscreen/AfterAtmosphere/VolumetricClouds.hlsl, Pack/Inject/Clouds/CloudDensity.hlsli, and the storm-object snapshot from phase 3 in ClientPlugin/Clouds/. The march is IsolatedMix. Weather is a 64×64×2 Texture3D: slice 0 is RGBA weather, slice 1 R is the surface fraction of the sea-to-air column. Shape and detail are pack SRVs. Fingerprint is in the fullscreen shader header. Anomaly extras stay 320 bytes. Do not add a second compile hook or steal Keen slots.

The test GPU is an RTX 2080 Ti that TDRs. No unbounded loops. Cap storm tests per sample.

Design a plan that:
- Uploads the phase-3 storm snapshot on the render thread, the same way other pack buffers are updated. Prefer a pack structured buffer the cloud pack already owns, or one new pack SRV if a slot is free. Do not put per-storm floats into extras. Name the slot you will use after you read the bind code.
- In the fine march, after the usual cloud density, adds an analytic term for the nearest one or two storms. Skip storms whose origin is far from the sample. Distance gates: anvil and hook on the order of 20–40 km, funnel plus debris skirt plus at most two subvortices inside about 2 km. Farther away, density is only the weather-map blob.
- Grounds the funnel on CloudSampleSurface (slice 1), not on a planet sphere and not on MaximumRadius.
- Shapes the funnel from the object's rope/cone/stovepipe/wedge fields: radius versus height, a rotating phase, subvortices as small offsets. A rope-out state narrows and lifts the funnel off the ground.
- Keeps the rest of the shell march, far cap, and octree behavior. Do not clip the march to MaximumRadius.
- Bumps the CloudDensity version and the VolumetricClouds fingerprint.
- Adds a debug view or reuses an existing one so a near funnel can be seen without guessing. If debug modes are packed in StepsFade.w, follow that packing. Do not break Off.

Out of scope: particles, lightning, voxel damage, refining the 64² grid, more than two storms per sample.

Deliver an implementation plan with the buffer layout, the HLSL functions, the uniform/SRV bind site, shader-compile steps, and todos. Dual-TFM Debug build and Tests/ShaderCompile.
```

## Phase 5 — Rain, debris, lightning

**Depends on:** phase 4, so there is a storm body to hang weather on.

**Outcome:** Close to a storm, the player sees a rain or hail shaft, a debris skirt that matches the funnel, and occasional lightning. ProtoManly's mod uses particles for rain, hail, and the near dust cloud, and only when you are close. Do the same. The shaft can be a cheap analytic column in the march if that is enough at distance. Particles are for the last kilometre or two. Nothing in this phase runs for every storm on the planet at full rate. The 2080 Ti is the budget.

**Not in this phase:** destroying voxels or grids.

### Plan-agent prompt

```text
Plan only. Do not implement. Do not edit Docs/StormRoadmap.md.

Repo: T:\Cursor Projects\VolumetricClouds
Phase: Storm roadmap phase 5, close-range rain, debris, and lightning.

Read Docs/StormRoadmap.md Phase 5 and the phase-4 storm draw path (shader plus the storm snapshot upload). ProtoManly's Weather renders rain, hail, and a near-field dust cloud as particles, and only when the camera is close to the storm. Lightning is frequent on supercells. This pack's march is a planetary shell on an unstable RTX 2080 Ti. Do not spawn a planet-wide particle system.

Design a plan that:
- Attaches precipitation to a storm object that is already a supercell or stronger. A distant storm keeps the analytic anvil only. Inside a couple of kilometres, add a rain or hail shaft. Hail belongs to the supercell core, rain to the downdraft side, matching that mod's layout closely enough to read.
- Draws the debris skirt with the funnel when the tornado is on the ground, and hides it when the funnel is aloft or roping out.
- Adds lightning as a short-lived segment on supercells, rate-limited, not every frame for every storm.
- Chooses particles versus a few extra march samples explicitly, with a cap on count and distance. Fail closed when the camera is far: zero particles.
- Does not write config from a HUD setter. If a density slider is added, mark dirty and flush from Update, the same rule as the rest of the pack.
- Stays off the Anomaly extras budget. Pack-owned buffers only.

Out of scope: voxel or grid damage, sirens, radar gameplay, refining the climate grid.

Deliver an implementation plan with budgets (max particles, max range, max lightning segments), the files to add, and todos. Dual-TFM Debug build. Shader compile if HLSL changes.
```

## Phase 6 — World damage

**Depends on:** phase 4 so the funnel has a ground position. Phase 5 is not required.

**Outcome:** An optional, rate-limited effect where a tornado on the ground can scour or break what it touches. ProtoManly's mod damages blocks by wind speed against a per-block strength, debarks trees, and scours the ground, and it only does that near the funnel. Space Engineers voxels and grids are not Minecraft blocks. This phase is a separate gameplay pass with a default that does not destroy a player's ship because they flew near a storm. Visual scour in the existing surface or a decal-like mark can land before any grid damage.

**Not in this phase:** more cloud lighting, a second march, multiplayer sync (call that out if the plan would need it).

### Plan-agent prompt

```text
Plan only. Do not implement. Do not edit Docs/StormRoadmap.md.

Repo: T:\Cursor Projects\VolumetricClouds
Phase: Storm roadmap phase 6, world interaction under a tornado.

Read Docs/StormRoadmap.md Phase 6 and the storm object's ground position from phase 3/4. ProtoManly's Weather applies EF-like wind to blocks near the funnel: wood fails before stone, violent tornadoes scour the ground, and the funnel ropes out. This game's world is voxels and grids, queried from a plugin, not from the cloud shader. The climate step must not call voxel edits. The march must not wait on damage.

Design a plan that:
- Runs damage, if at all, from the game thread on a slow cadence, only for tornadoes in the on-ground state, only inside the funnel radius at the surface, and only when the camera or the funnel is in a bounded range. Cap edits per tick.
- Offers a visual ground mark first (a short-lived scour cue that does not delete voxels) and treats voxel or grid damage as a second, default-off switch.
- Does not serialize that switch from a Rich HUD slider callback. Dirty flag, flush from Update.
- States the Keen API you will actually call after you look it up with the se-dev skills. If grid damage is unsafe or needs server authority, stop at the visual mark and say so.
- Never runs from the shader, the render thread, or the 4 Hz climate loop.

Out of scope: radar, sirens, weather balloons, changing IsolatedMix, Anomaly extras.

Deliver an implementation plan that leads with the visual mark, gates real destruction behind a default-off setting, and lists todos. Dual-TFM Debug build only if C# changes. No shader compile unless the mark is a shader change, in which case keep it tiny and version the fingerprint.
```

## Suggested order

1. Pressure centers, so motion is already circulation.
2. Hurricanes, while everything is still a stamp.
3. Storm objects, so stages exist before any shader work.
4. Analytic march, the actual tornado picture.
5. Close particles and lightning.
6. World damage, defaulting to a mark.

Phases 5 and 6 can swap. Do not start phase 4 until phase 3's snapshot layout is frozen. Do not refine the 64×64 grid in any phase.
