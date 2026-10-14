// helpers: CloudDensity.hlsli v82
// IsolatedMix loop is the Nubis ray-march (density → light → integrate).
// The puff is the 128³ Perlin-Worley at a few hundred metres. Weather only
// biases altitude, depth, and how many billows survive. It is not the cloud.
#ifndef CLOUD_DENSITY_HLSLI
#define CLOUD_DENSITY_HLSLI

float CloudRemap01(float x, float a, float b)
{
    return saturate((x - a) / max(b - a, 1e-5));
}

// Amount = fill inside a synoptic weather system. Spacing is the NDF tile.
// Do not harden R to puff-scale cookies — that extruded 2D footprints into
// radial blades (height window >> weather XZ) and hid the 3D Worley profile.
float CloudWeatherMask(float weatherR)
{
    return CloudRemap01(weatherR, 0.10, 0.88);
}

float CloudWeatherFill(float amount)
{
    return saturate((clamp(amount, 0.05, 2.0) - 0.05) / 1.10);
}

float CloudWeatherAmount(float mask, float fill)
{
    // Amount remaps inside the mask only. Empty stays empty.
    return saturate(mask * lerp(0.0, 1.05, fill));
}

bool CloudWeatherEmpty(float weatherR)
{
#ifdef ANOMALY_PASS_EXTRAS_CB_HLSLI
    return AnomalyVolumeIsEmpty(weatherR);
#else
    return weatherR < 0.38;
#endif
}

float CloudHillRadius(float inner)
{
    return VolumeHdr.z > 1.0 ? VolumeHdr.z : inner;
}

float CloudVisualCeil(float hill)
{
    float a = VolumeHdr.w;
    return a > hill + 40.0 ? a : hill * 1.12;
}

#ifndef CLOUD_VIEW_LOD_FAR
#define CLOUD_VIEW_LOD_FAR 40000.0
#endif

// 0 through visual air (CamToShell == 0). 1 at CLOUD_VIEW_LOD_FAR metres
// past that. Same IsolatedMix; fade detail, do not flip to a second system.
float CloudViewLod()
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    return AnomalyVolumeViewLod(CamToShell, CLOUD_VIEW_LOD_FAR);
#else
    return saturate(max(CamToShell, 0.0) / CLOUD_VIEW_LOD_FAR);
#endif
}

float CloudShapeLodSize(float volumeSize)
{
    // Debug and legacy callers. The drawn puff is CloudPuffMetres.
    float configured = CloudShapeScale > 0.05 ? clamp(CloudShapeScale, 0.5, 3.0) : 1.0;
    return max(volumeSize, 2000.0) * configured;
}

float CloudDetailLodSize()
{
    float configured = CloudDetailScale > 0.05 ? clamp(CloudDetailScale, 0.25, 4.0) : 1.0;
    return 100.0 * configured;
}

// Pixel footprint in metres. Nyquist for the 128³, not a distance hack that
// landed mip 3 (250 m texels) at 80 km and turned 1 km billows into blocks.
float CloudPixelFootprint(float sampleDist)
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    float proj = max(max(AnomalyProjScale.x, AnomalyProjScale.y), 1e-4);
    float pixelAngle = max(AnomalyInvPassSize.x, AnomalyInvPassSize.y) * 2.0 / proj;
    return max(sampleDist, 1.0) * max(pixelAngle, 1e-5);
#else
    return max(sampleDist, 1.0) * 0.0013;
#endif
}

float CloudNoiseLod(float sampleDist, float sampleStep, float worldPeriod, float textureDim, float maxMip)
{
    float texel = max(worldPeriod, 1.0) / max(textureDim, 1.0);
    float filter = max(max(sampleStep, 1.0), CloudPixelFootprint(sampleDist));
    return clamp(log2(max(filter / texel, 1.0)), 0.0, max(maxMip - 2.0, 0.0));
}

float CloudAirHeight(float r, float hill, float airTop)
{
    return (r - hill) / max(airTop - hill, 200.0);
}

float CloudTerrainFloor(float inner, float hill)
{
    float terrain = CloudTerrainRadius;
    if (terrain > 1.0 && terrain < hill - 20.0)
        return terrain;
    return hill * 0.92;
}

// Packed inner/outer are the density gate, one unit for Min and Max:
// 0 = sea, 1 = lerp(hills, visual ceil, AtmosphereFit). Above 1 continues
// past that lid in the same metres. The gate clips type decks. It is not
// the cloud shape. March a pad outside it so the sphere is empty air.
// GAP: Slice AP — VisualCeil equals AirTop; fit is the pack stand-in for
// the optical limb.
void CloudGateRadii(float inner, float outer, out float gateInner, out float gateOuter)
{
    gateInner = inner;
    gateOuter = max(outer, inner + 80.0);
}

// Packed Min. 0 = Keen AverageRadius (datum / sea), not hill tops.
float CloudColumnFloor(float inner)
{
    return inner;
}

// Geometric sun limb is the solid planet (sea / valleys), not hill tops
// and not packed Min. Min as planetR sat the vis=0 sphere in the Low deck
// (circular line from orbit). MaximumRadius zeroed Low/Mid after Min 0
// moved to sea.
float CloudSunPlanetR(float inner)
{
    float hill = CloudHillRadius(inner);
    float terrain = CloudTerrainRadius;
    float sea = CloudSeaRadius;
    if (sea <= 1.0)
        sea = terrain > 1.0 ? terrain : hill * 0.92;
    float floorR = (terrain > 1.0 && terrain < hill - 20.0) ? terrain : sea;
    float occluder = min(sea, floorR);
    return occluder > 1.0 ? occluder : hill * 0.92;
}

float CloudSeaOrTerrain(float inner)
{
    float sea = CloudSeaRadius;
    if (sea > 1.0)
        return sea;
    float hill = CloudHillRadius(inner);
    return CloudTerrainFloor(inner, hill);
}

void CloudOccupiedRadii(float inner, float outer, out float occInner, out float occOuter)
{
    float gateIn, gateOut;
    CloudGateRadii(inner, outer, gateIn, gateOut);
    float pad = 1200.0;
    float hill = CloudHillRadius(inner);
    float sea = CloudSeaOrTerrain(inner);
    float floorR = CloudTerrainFloor(inner, hill);
    // Min at sea includes valleys (stratus around mountains). A raised min
    // starts in the empty pad, so the floor is not a lit sphere.
    occInner = gateIn <= sea + 50.0 ? min(gateIn, floorR) : max(gateIn - pad, 1.0);
    occOuter = gateOut + pad;
}

float CloudAirSpan(float inner)
{
    float sea = CloudSeaOrTerrain(inner);
    float airTop = CloudVisualCeil(CloudHillRadius(inner));
    return max(airTop - sea, 200.0);
}

float CloudKmToH(float km)
{
    return (km * 1000.0) / CloudAirSpan(CenterInner.w);
}

// WMO 0–1 on the full sea→air column. Min/Max are clamps, not this domain.
float CloudReferenceHeight(float3 worldPos, float3 center, float inner)
{
    float r = length(worldPos - center);
    float sea = CloudSeaOrTerrain(inner);
    float airTop = CloudVisualCeil(CloudHillRadius(inner));
    return CloudAirHeight(r, sea, max(airTop, sea + 200.0));
}

float CloudAltitude01(float3 worldPos, float3 center, float inner, float outer)
{
    return CloudReferenceHeight(worldPos, center, inner);
}

bool CloudColumnOvershoots(float inner, float outer)
{
    float hill = CloudHillRadius(inner);
    return outer > CloudVisualCeil(hill) + 40.0;
}

float CloudVisualH(float inner, float outer)
{
    float sea = CloudSeaOrTerrain(inner);
    float airTop = CloudVisualCeil(CloudHillRadius(inner));
    return CloudAirHeight(airTop, sea, max(airTop, sea + 200.0));
}

float CloudTypeThickness(int id)
{
    float t = 1.0;
    if (id == 0) t = CloudTypeThick0;
    else if (id == 1) t = CloudTypeThick1;
    else if (id == 2) t = CloudTypeThick2;
    else if (id == 3) t = CloudTypeThick3;
    else if (id == 4) t = CloudTypeThick4;
    else if (id == 5) t = CloudTypeThick5;
    else if (id == 6) t = CloudTypeThick6;
    else if (id == 7) t = CloudTypeThick7;
    else if (id == 8) t = CloudTypeThick8;
    else t = CloudTypeThick9;
    return t > 0.05 ? t : 1.0;
}

float CloudTypeShapeScale(int id)
{
    float s = 1.0;
    if (id == 0) s = CloudTypeShape0;
    else if (id == 1) s = CloudTypeShape1;
    else if (id == 2) s = CloudTypeShape2;
    else if (id == 3) s = CloudTypeShape3;
    else if (id == 4) s = CloudTypeShape4;
    else if (id == 5) s = CloudTypeShape5;
    else if (id == 6) s = CloudTypeShape6;
    else if (id == 7) s = CloudTypeShape7;
    else if (id == 8) s = CloudTypeShape8;
    else s = CloudTypeShape9;
    return s > 0.05 ? s : 1.0;
}

// Thickness 1 = native type depth in kilometres (hundreds of m to a few km),
// not a fraction of the whole atmosphere. Type LUT owns the slab position.
void CloudPlaceTypeWindow(int typeId, inout float nativeKm, inout float shapeMul)
{
    nativeKm *= CloudTypeThickness(typeId);
    shapeMul *= CloudTypeShapeScale(typeId);
}

void CloudGateHRange(out float hLo, out float hHi)
{
    float gateIn, gateOut;
    CloudGateRadii(CenterInner.w, SunOuter.w, gateIn, gateOut);
    float sea = CloudSeaOrTerrain(gateIn);
    float top = max(CloudVisualCeil(CloudHillRadius(gateIn)), sea + 200.0);
    hLo = CloudAirHeight(gateIn, sea, top);
    hHi = CloudAirHeight(gateOut, sea, top);
}

// Rounded type hump. A plateau (flat 1 from ~8% to ~72% of the window)
// extruded Perlin-Worley into flat-topped blocks.
float CloudHeightGradient(float h, float lo, float hi, int typeId)
{
    float span = max(hi - lo, 1e-4);
    float x = saturate((h - lo) / span);
    float peak = 0.42;
    float sigma = 0.46;
    if (typeId == 0) { peak = 0.28; sigma = 0.34; }
    else if (typeId == 1) { peak = 0.38; sigma = 0.42; }
    else if (typeId == 2) { peak = 0.40; sigma = 0.48; }
    else if (typeId == 3) { peak = 0.36; sigma = 0.50; }
    else if (typeId == 4) { peak = 0.46; sigma = 0.55; }
    else if (typeId == 5) { peak = 0.48; sigma = 0.40; }
    else if (typeId == 6) { peak = 0.50; sigma = 0.38; }
    else if (typeId == 7) { peak = 0.55; sigma = 0.32; }
    else if (typeId == 8) { peak = 0.52; sigma = 0.36; }
    else { peak = 0.58; sigma = 0.30; }
    float d = (x - peak) / max(sigma, 0.05);
    float g = exp(-d * d);
    // The window is this spawn's own soft extent, already inset from the
    // gate. Density falls off at both faces. A shoulder here filled every
    // spawn from its floor to its lid and read as one slab.
    float skirtLo = 0.16;
    float skirtHi = 0.84;
    if (typeId == 2 || typeId == 4 || typeId == 6)
    {
        skirtLo = 0.08;
        skirtHi = 0.70;
    }
    else if (typeId >= 7)
    {
        skirtLo = 0.20;
        skirtHi = 0.78;
    }
    float skirt = smoothstep(0.0, skirtLo, x) * (1.0 - smoothstep(skirtHi, 1.0, x));
    g *= skirt;
    // Cb anvil: the top stays filled and shelves instead of rounding off.
    if (typeId == 4 && x > 0.58)
        g = max(g, smoothstep(0.58, 0.74, x) * (1.0 - smoothstep(0.92, 1.0, x)) * 0.92);
    return g;
}

float CloudTypeZStretch(int id)
{
    // Nubis sample_pos is isotropic world metres. Vertical extent is the
    // dimensional profile, not Y-squashed noise (that painted spherical
    // sheets along the hills). id kept so FXC does not warn.
    return id >= 0 ? 1.0 : 1.0;
}

// Soft clamp. Min 0 does not fade the sea floor (the planet is the floor).
// A raised min and the max lid feather ~0.9 km so the gate is not a shell.
float CloudColumnEdgeFade(float h)
{
    float hLo, hHi;
    CloudGateHRange(hLo, hHi);
    float span = max(hHi - hLo, 1e-4);
    float pad = min(CloudKmToH(0.9), span * 0.45);
    float floorFade = hLo <= 0.02 ? 1.0 : CloudRemap01(h, hLo, hLo + pad);
    return floorFade * (1.0 - CloudRemap01(h, hHi - pad, hHi));
}

// Weather picks a preferred altitude and depth. The hump is a bias, not a
// box the noise has to fill.
float CloudFormationProfile(float h, float mid, float depthH, int typeId)
{
    float sigma = max(depthH * 0.65, CloudKmToH(0.40));
    if (typeId == 4)
        sigma = max(sigma, CloudKmToH(0.85));
    float d = (h - mid) / sigma;
    return exp(-d * d) * CloudColumnEdgeFade(h);
}

int CloudClassifyConvective(float base01, float thickKm)
{
    if (thickKm > 2.15 && base01 < 0.42)
        return 4;
    if (base01 < 0.12 && thickKm < 1.4)
        return 0;
    if (base01 < 0.22 && thickKm < 1.8)
        return 1;
    if (base01 < 0.40 && thickKm > 1.55)
        return 2;
    if (base01 < 0.36)
        return 3;
    if (base01 > 0.55)
        return 5;
    return 6;
}

int CloudClassifyCirrus(float thickKm)
{
    if (thickKm > 1.35)
        return 8;
    if (thickKm < 0.75)
        return 9;
    return 7;
}

// Camera-relative metres. Min/Max/Fit still own the radial column.
float CloudViewRangeFade(float3 samplePos)
{
    float range = CloudHorizonRange;
    if (range < 80.0)
        return 1.0;
    return 1.0 - CloudRemap01(length(samplePos), range * 0.72, range);
}

// GAP: Slice AT — AnomalyPlanetShellMetresFromDir when IsolatedMix includes
// AnomalyFullscreen; Medium keeps the same east/north metres.
// Geographic only (puff hash, not the shape tile). cos(lat) is not floored.
float2 CloudSphereMeters(float3 dir, float radius)
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    return AnomalyPlanetShellMetresFromDir(dir, radius);
#else
    dir = normalize(dir);
    float lat = asin(clamp(dir.y, -1.0, 1.0));
    float lon = atan2(dir.x, dir.z);
    return float2(lon * cos(lat), lat) * max(radius, 1.0);
#endif
}

// Azimuthal equidistant from +Y. The pole is one point. Pass a Y-flipped
// dir to center on the south pole. GAP: Slice AT — AnomalyPlanetShapeArcMetres.
float2 CloudShapeArcMetres(float3 dir, float radius)
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    return AnomalyPlanetShapeArcMetres(dir, radius);
#else
    dir = normalize(dir);
    float theta = acos(clamp(dir.y, -1.0, 1.0));
    float phi = atan2(dir.x, dir.z);
    float arc = theta * max(radius, 1.0);
    return float2(sin(phi), cos(phi)) * arc;
#endif
}

float CloudShapeNorthWeight(float3 dir)
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    return AnomalyPlanetShapeNorthWeight(dir);
#else
    dir = normalize(dir);
    return smoothstep(-0.06, 0.06, dir.y);
#endif
}

// Nubis sample_pos: (east metres, altitude, north metres) on the nearer pole.
float3 CloudNubisSamplePos(float3 worldPos, float3 center, float zStretch)
{
    float hill = CloudHillRadius(CenterInner.w);
    float3 rel = worldPos - center;
    float r = max(length(rel), 1.0);
    float3 dir = rel / r;
    float3 chart = dir.y >= 0.0 ? dir : float3(dir.x, -dir.y, dir.z);
    float2 xz = CloudShapeArcMetres(chart, hill);
    float3 p = float3(xz.x, r - hill, xz.y);
    p.y *= max(zStretch, 0.35);
    return p;
}

// Date line of atan2. Weight is 0 on −Z and 1 by 0.06 rad. The second
// chart yaws phi 90° at the same point; it does not move the sample.
float CloudShellChartWeight(float3 dir)
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    return AnomalyPlanetShellChartWeight(dir);
#else
    dir = normalize(dir);
    float lon = atan2(dir.x, dir.z);
    return smoothstep(0.0, 0.06, 3.14159265 - abs(lon));
#endif
}

float3 CloudShellChartDir(float3 dir)
{
#ifdef ANOMALY_FULLSCREEN_HLSLI
    return AnomalyPlanetShellChartDir(dir);
#else
    return float3(dir.z, dir.y, -dir.x);
#endif
}

float3 CloudShapeChartUv(float3 dir, float altitude, float hill, float volumeSize,
    float3 wind, float3 phase, float zStretch)
{
    float2 xz = CloudShapeArcMetres(dir, hill);
    float3 p = float3(xz.x, altitude * max(zStretch, 0.35), xz.y);
    return p / max(volumeSize, 1.0) + wind + phase;
}

// sin/cos of longitude is continuous across the old date line. The only
// blend is the equator, where the nearer pole changes. A yawed second
// sample mixed two tiles along a meridian that is no longer a cut.
float4 CloudSampleShapeChart(Texture3D tex, float3 dir, float altitude, float hill, float volumeSize,
    float3 wind, float3 phase, float zStretch, float lod)
{
    return tex.SampleLevel(AnomalyWrapSampler,
        CloudShapeChartUv(dir, altitude, hill, volumeSize, wind, phase, zStretch), lod);
}

float4 CloudSampleShellTex(Texture3D tex, float3 worldPos, float3 center, float volumeSize,
    float3 wind, float3 phase, float zStretch, float lod)
{
    float3 rel = worldPos - center;
    float r = max(length(rel), 1.0);
    float3 dir = rel / r;
    float hill = CloudHillRadius(CenterInner.w);
    float altitude = r - hill;
    float northW = CloudShapeNorthWeight(dir);
    float4 north = CloudSampleShapeChart(tex, dir, altitude, hill, volumeSize, wind, phase, zStretch, lod);
    if (northW > 0.999)
        return north;
    float3 south = float3(dir.x, -dir.y, dir.z);
    float4 southS = CloudSampleShapeChart(tex, south, altitude, hill, volumeSize, wind, phase, zStretch, lod);
    if (northW < 0.001)
        return southS;
    return lerp(southS, north, northW);
}

// One equirectangular climate map (lon, lat). Texels are the live cloud
// field (coverage and type). Not a repeating metre tile. Wind stays on
// the 3D shape; sliding this map would drag orography off the terrain.
// scale/wind/phase kept so call sites stay valid.
float3 CloudWeatherUv(float3 worldPos, float3 center, float scale, float3 wind, float3 phase)
{
    float3 dir = normalize(worldPos - center);
    float lat = asin(clamp(dir.y, -1.0, 1.0));
    float lon = atan2(dir.x, dir.z);
    float u = lon * 0.15915494 + 0.5;
    float v = saturate(lat * 0.31830989 + 0.5);
    v = lerp(0.5 / 64.0, 1.0 - 0.5 / 64.0, v);
    u += (scale + wind.x + phase.x) * 0.0;
    // Center of slice 0. Slice 1 (z = 0.75) is the surface fraction.
    return float3(u, v, 0.25);
}

float CloudSampleSurface(Texture3D weatherTex, float3 worldPos, float3 center)
{
    float3 uvw = CloudWeatherUv(worldPos, center, 1.0, 0.0, 0.0);
    uvw.z = 0.75;
    return weatherTex.SampleLevel(AnomalyWrapSampler, uvw, 0.0).r;
}

// 2D 64² map — no mips. Lod 0 always so coverage cannot empty from orbit.
float CloudWeatherLod(float floorLod)
{
    return floorLod * 0.0;
}

// Periodic longitude. East-metre sin() jumps on the date line and offsets
// the climate map by a couple of texels along that meridian.
float2 CloudWeatherWarp(float3 worldPos, float3 center)
{
    float3 dir = normalize(worldPos - center);
    float lat = asin(clamp(dir.y, -1.0, 1.0));
    float lon = atan2(dir.x, dir.z);
    float wx = sin(lon * 3.0) * sin(lat * 2.0 + 1.3);
    float wy = sin(lon * 2.0 + 0.7) * sin(lat * 4.0);
    return float2(wx, wy) * (1.1 / 64.0);
}

float4 CloudSampleWeather(Texture3D weatherTex, float3 worldPos, float3 center, float inner, float3 wind, float lod)
{
    float scale = max(WeatherScale, 1.0);
    float3 uvw = CloudWeatherUv(worldPos, center, scale, wind, 0.0);
    uvw.xy += CloudWeatherWarp(worldPos, center);
    // Latitude does not wrap. The shell sampler wraps V, and the extra
    // taps reach 1.4/64, so stay inside that pad or arctic blends into antarctic.
    uvw.y = clamp(uvw.y, 1.5 / 64.0, 1.0 - 1.5 / 64.0);
    // Five taps, not on the texel axes. The 64² field stays synoptic;
    // the march must not extrude those texels.
    float o = 1.4 / 64.0;
    float4 s = weatherTex.SampleLevel(AnomalyWrapSampler, uvw, 0.0);
    s += weatherTex.SampleLevel(AnomalyWrapSampler, uvw + float3(o, 0.40 * o, 0.0), 0.0);
    s += weatherTex.SampleLevel(AnomalyWrapSampler, uvw + float3(-0.80 * o, 0.60 * o, 0.0), 0.0);
    s += weatherTex.SampleLevel(AnomalyWrapSampler, uvw + float3(0.50 * o, -o, 0.0), 0.0);
    s += weatherTex.SampleLevel(AnomalyWrapSampler, uvw + float3(-0.30 * o, -0.90 * o, 0.0), 0.0);
    return s * 0.2;
}

float4 CloudSampleWeatherSharp(Texture3D weatherTex, float3 worldPos, float3 center,
    float inner, float3 wind, float typeLod)
{
    // Same synoptic tap for RGBA. Resampling G/B at 0.32× tile aliased into
    // the streaks in the Weather Type view and slid the height window with them.
    if (typeLod < -1.0 || inner < -1.0)
        return 0;
    return CloudSampleWeather(weatherTex, worldPos, center, inner, wind, 0.0);
}

// 0 St  1 Sc  2 Cu  3 Ns  4 Cb  5 As  6 Ac  7 Ci  8 Cs  9 Cc
// centerH is 0–1 on the sea→air column (EarthLike hills sit ~0.50).
// nativeKm is deck depth. Weather G does not replace this LUT.
void CloudTypeSpec(int id, float convection, float terrainHug,
    out float centerH, out float nativeKm, out float carveAmt, out float shapeMul)
{
    centerH = 0.48;
    nativeKm = 1.6;
    carveAmt = 0.62;
    shapeMul = 1.0;
    if (id == 0)
    {
        centerH = 0.10;
        nativeKm = 0.70;
        carveAmt = 0.42;
        shapeMul = 1.25;
    }
    else if (id == 1)
    {
        centerH = 0.34;
        nativeKm = 1.40;
        carveAmt = 0.52;
        shapeMul = 1.10;
    }
    else     if (id == 2)
    {
        centerH = 0.52;
        nativeKm = 2.20;
        carveAmt = 0.72;
        shapeMul = 1.00;
    }
    else if (id == 3)
    {
        centerH = 0.28;
        nativeKm = 1.80;
        carveAmt = 0.48;
        shapeMul = 1.20;
    }
    else if (id == 4)
    {
        centerH = 0.46;
        nativeKm = 3.20 + saturate(convection) * 0.8;
        carveAmt = 0.78;
        shapeMul = 0.92;
    }
    else if (id == 5)
    {
        centerH = 0.64;
        nativeKm = 1.30;
        carveAmt = 0.50;
        shapeMul = 0.85;
    }
    else if (id == 6)
    {
        centerH = 0.58;
        nativeKm = 1.05;
        carveAmt = 0.58;
        shapeMul = 0.72;
    }
    else if (id == 7)
    {
        centerH = 0.78;
        nativeKm = 0.80;
        carveAmt = 0.70;
        shapeMul = 0.48;
    }
    else if (id == 8)
    {
        centerH = 0.72;
        nativeKm = 0.90;
        carveAmt = 0.46;
        shapeMul = 0.70;
    }
    else
    {
        centerH = 0.82;
        nativeKm = 0.70;
        carveAmt = 0.66;
        shapeMul = 0.58;
    }
    if (terrainHug > 0.5 && id == 0)
    {
        centerH = 0.07;
        nativeKm = 0.80;
    }
}

// One cloud's own height. Smooth between ~5 km neighbors, so formations
// separate vertically without a sky-wide wave or a hard cell edge.
float CloudPuffChart(float3 dir, float metres, float salt)
{
    float2 sph = CloudSphereMeters(dir, max(CloudHillRadius(CenterInner.w), 1.0));
    float2 g = sph / max(metres, 400.0);
    float2 cell = floor(g);
    float2 f = g - cell;
    f = f * f * (3.0 - 2.0 * f);
    float h00 = frac(sin(dot(cell, float2(127.1, 311.7)) + salt) * 43758.5453);
    float h10 = frac(sin(dot(cell + float2(1.0, 0.0), float2(127.1, 311.7)) + salt) * 43758.5453);
    float h01 = frac(sin(dot(cell + float2(0.0, 1.0), float2(127.1, 311.7)) + salt) * 43758.5453);
    float h11 = frac(sin(dot(cell + float2(1.0, 1.0), float2(127.1, 311.7)) + salt) * 43758.5453);
    return lerp(lerp(h00, h10, f.x), lerp(h01, h11, f.x), f.y);
}

float CloudPuff01(float3 dir, float metres, float salt)
{
    float w = CloudShellChartWeight(dir);
    float a = CloudPuffChart(dir, metres, salt);
    if (w > 0.999)
        return a;
    return lerp(CloudPuffChart(CloudShellChartDir(dir), metres, salt), a, w);
}

// Type centerH and nativeKm are this cloud's altitude and depth.
// Min/Max only reject a cloud that would cross them.
void CloudLocalColumn(float3 worldPos, float3 center, float4 weather, bool cirrus,
    float terrainHug, float surfaceH, out int typeId, out float lo, out float hi,
    out float carveAmt, out float shapeMul)
{
    float hLo, hHi;
    CloudGateHRange(hLo, hHi);
    float3 n = (worldPos - center) / max(length(worldPos - center), 1.0);

    float thickKm;
    if (cirrus)
    {
        thickKm = lerp(0.5, 1.4, weather.b);
        typeId = CloudClassifyCirrus(thickKm);
    }
    else
    {
        float typeHint = saturate(weather.g);
        thickKm = lerp(0.55, 2.35, weather.b);
        typeId = CloudClassifyConvective(typeHint, thickKm);
    }

    float centerH, nativeKm;
    CloudTypeSpec(typeId, weather.b, terrainHug, centerH, nativeKm, carveAmt, shapeMul);
    CloudPlaceTypeWindow(typeId, nativeKm, shapeMul);
    float puff = CloudPuff01(n, 5200.0, (float)typeId + (cirrus ? 9.0 : 1.0));
    float deck = CloudKmToH(max(nativeKm, 0.35)) * lerp(0.85, 1.15, puff);

    float margin = CloudKmToH(0.30);
    float roomLo = hLo + margin;
    float roomHi = hHi - margin;
    float room = roomHi - roomLo;
    if (room < CloudKmToH(0.35))
    {
        lo = 0.0;
        hi = 0.0;
        return;
    }
    // A weather strip is one G/B, so one type. If that deck fills the gate,
    // every column clamps to the same mid and the strip is a flat sheet.
    // Leave room so each ~5 km cloud can sit at its own altitude.
    if (deck > room * 0.55)
        deck = room * 0.55;

    float mid = centerH;
    if (!cirrus && typeId <= 3)
    {
        float floorH = saturate(surfaceH) + CloudKmToH(0.15);
        float lo0 = mid - deck * 0.5;
        if (lo0 < floorH)
            mid += floorH - lo0;
    }
    float spread = max(deck * 0.45, room * 0.70);
    mid += (puff - 0.5) * spread;
    float half = deck * 0.5;
    mid = clamp(mid, roomLo + half, roomHi - half);
    lo = mid - half;
    hi = mid + half;
    if (hi <= hLo || lo >= hHi || hi <= lo + 1e-4)
    {
        lo = 0.0;
        hi = 0.0;
    }
}

// 2D weather only. Height is not a skip axis (that cut a sphere).
float CloudSkipOccupancy(float4 weather)
{
    return max(weather.r, weather.a);
}

float4 CloudSampleShapeLod(Texture3D shapeTex, float3 worldPos, float3 center, float volumeSize,
    float3 wind, float4 weather, float zStretch, float lod)
{
    // Nubis base sampler. Weather phase must stay sub-puff (synoptic G/B).
    // A 0.35 wrap jump at weather texels sheared 3D isosurfaces into slabs.
    float3 phase = (weather.gba - 0.5) * 0.08;
    return CloudSampleShellTex(shapeTex, worldPos, center, volumeSize, wind, phase, zStretch, lod);
}

float4 CloudSampleShape(Texture3D shapeTex, float3 worldPos, float3 center, float volumeSize,
    float sampleStep, float3 wind, float4 weather, float zStretch)
{
    float lod = CloudNoiseLod(length(worldPos), sampleStep, volumeSize, 128.0, 7.0);
    return CloudSampleShapeLod(shapeTex, worldPos, center, volumeSize, wind, weather, zStretch, lod);
}

// One shared Perlin-Worley tile. R is ~4 cells (the billow). G ~8, B ~16, A ~32.
// Nubis does not add those octaves. They are the remap floor that carves R.
// Type picks which octave and how hard. A geographic map does not.
float CloudWorleyFbm(float4 n)
{
    return saturate(n.g * 0.625 + n.b * 0.250 + n.a * 0.125);
}

// Mild tile scale. Distinct silhouettes come from the floor and the height
// hump, not from stretching one pattern by 3×.
float CloudTypeFrequency(int typeId, bool cirrus)
{
    if (cirrus)
        return 1.35;
    if (typeId <= 1)
        return 1.10;
    if (typeId == 2)
        return 0.90;
    if (typeId == 4)
        return 0.80;
    if (typeId == 6 || typeId == 9)
        return 1.25;
    return 1.0;
}

// 1.1.0 puff. Worley-4 on this period is ~180 m. A 4 km tile made one
// billow per weather texel, so the map became the cloud.
float CloudPuffMetres(int typeId, bool cirrus)
{
    float configured = CloudShapeScale > 0.05 ? clamp(CloudShapeScale, 0.5, 3.0) : 1.0;
    return 720.0 * configured * CloudTypeFrequency(typeId, cirrus);
}

// Remap floor in 0–1. Higher floor keeps only the cores (sheets, veils).
// Lower floor keeps the whole Perlin-Worley billow (cumulus, nimbostratus).
float CloudShapeFloor(float4 n, float xh, int typeId, bool cirrus)
{
    float g = saturate(n.g);
    float b = saturate(n.b);
    float a = saturate(n.a);
    float fbm = CloudWorleyFbm(n);
    float freq = fbm;
    float amt = 0.55;
    if (cirrus)
    {
        freq = lerp(b, a, 0.70);
        amt = 0.78;
    }
    else if (typeId <= 0)
    {
        freq = lerp(b, a, 0.40);
        amt = 0.72;
    }
    else if (typeId == 1)
    {
        freq = lerp(g, b, 0.45);
        amt = 0.58;
    }
    else if (typeId == 2)
    {
        freq = fbm;
        amt = 0.60;
    }
    else if (typeId == 3)
    {
        freq = g;
        amt = 0.28;
    }
    else if (typeId == 4)
    {
        freq = fbm;
        amt = lerp(0.68, 0.20, smoothstep(0.60, 0.90, xh));
    }
    else if (typeId == 5)
    {
        freq = lerp(g, b, 0.55);
        amt = 0.46;
    }
    else if (typeId == 6)
    {
        freq = lerp(g, b, 0.80);
        amt = 0.64;
    }
    else if (typeId == 7)
    {
        freq = lerp(b, a, 0.75);
        amt = 0.76;
    }
    else if (typeId == 8)
    {
        freq = a;
        amt = 0.34;
    }
    else
    {
        freq = lerp(b, a, 0.55);
        amt = 0.68;
    }
    return lerp(0.06, freq, amt);
}

float CloudShapeSignal(float4 n, float xh, int typeId, bool cirrus)
{
    return CloudRemap01(saturate(n.r), CloudShapeFloor(n, xh, typeId, cirrus), 1.0);
}

float CloudEvalField(
    float3 worldPos,
    float3 center,
    float3 wind,
    float h,
    float coverageScale,
    float volumeSize,
    float sampleStep,
    float layerWeight,
    float coverageChannel,
    bool cirrus,
    float4 weather,
    float terrainHug,
    float surfaceH,
    Texture3D shapeTex)
{
    if (layerWeight < 0.02)
        return 0.0;
    int typeId;
    float lo, hi, carveAmt, shapeMul;
    CloudLocalColumn(worldPos, center, weather, cirrus, terrainHug, surfaceH, typeId, lo, hi, carveAmt, shapeMul);
    if (hi <= lo + 1e-4)
        return 0.0;
    float mid = (lo + hi) * 0.5;
    float depthH = max(hi - lo, CloudKmToH(0.35));
    float profile = CloudFormationProfile(h, mid, depthH, typeId);
    if (profile < 1e-3)
        return 0.0;
    float coverage = CloudWeatherAmount(CloudWeatherMask(coverageChannel), CloudWeatherFill(coverageScale))
        * saturate(layerWeight);
    if (coverage < 0.04)
        return 0.0;
    // Coverage only moves the carve threshold. Full cover still keeps the
    // billow; it does not fill the type window.
    float size = CloudPuffMetres(typeId, cirrus) * max(shapeMul, 0.35);
    float stretch = CloudTypeZStretch(typeId);
    float lod = CloudNoiseLod(length(worldPos), sampleStep, size, 128.0, 7.0);
    float xh = saturate((h - lo) / max(hi - lo, 1e-4));
    float4 nBody = CloudSampleShapeLod(shapeTex, worldPos, center, size,
        wind, weather, stretch, lod);
    float shape = CloudShapeSignal(nBody, xh, typeId, cirrus);
    float threshold = saturate(lerp(0.58, 0.16, coverage) + 0.08 * saturate(carveAmt) * xh);
    return CloudRemap01(shape, threshold, 1.0) * profile * lerp(0.45, 1.0, coverage);
}

// Weather-system potential for coarse search. Not the puff silhouette —
// IsolatedMix fine steps own that with CloudEvalField.
float CloudFieldHeightOnly(float3 worldPos, float3 center, float h, float coverageScale,
    float layerWeight, float coverageChannel, bool cirrus, float4 weather, float terrainHug, float surfaceH)
{
    if (layerWeight < 0.02)
        return 0.0;
    int typeId;
    float lo, hi, carveAmt, shapeMul;
    CloudLocalColumn(worldPos, center, weather, cirrus, terrainHug, surfaceH, typeId, lo, hi, carveAmt, shapeMul);
    if (hi <= lo + 1e-4)
        return 0.0;
    float mid = (lo + hi) * 0.5;
    float profile = CloudFormationProfile(h, mid, max(hi - lo, CloudKmToH(0.35)), typeId);
    float coverage = CloudWeatherAmount(CloudWeatherMask(coverageChannel), CloudWeatherFill(coverageScale))
        * saturate(layerWeight);
    return saturate(coverage * profile);
}

float CloudBaseFromWeather(
    float3 worldPos,
    float3 center,
    float inner,
    float outer,
    float coverageScale,
    float volumeSize,
    float sampleStep,
    float3 wind,
    float cirrusStrength,
    float4 layerPeaks,
    float4 layerWidths,
    float terrainHug,
    Texture3D shapeTex,
    Texture3D weatherTex,
    out float4 weather)
{
    float lod = CloudWeatherLod(0.0);
    weather = CloudSampleWeatherSharp(weatherTex, worldPos, center, inner, wind, lod);
    float surfaceH = CloudSampleSurface(weatherTex, worldPos, center);
    float h = CloudAltitude01(worldPos, center, inner, outer);
    float iceWeight = LayerCirrus;
    if (cirrusStrength > iceWeight)
        iceWeight = cirrusStrength;
    float d = CloudEvalField(worldPos, center, wind, h, coverageScale, volumeSize, sampleStep,
        LayerConvective, weather.r, false, weather, terrainHug, surfaceH, shapeTex);
    d = max(d, CloudEvalField(worldPos, center, wind, h, coverageScale, volumeSize, sampleStep,
        iceWeight, weather.a, true, weather, terrainHug, surfaceH, shapeTex));
    return saturate(d);
}

// Weather + height only (sun column / dest-shadow). No full shape (TDR budget).
float CloudWeatherDensity(
    float3 worldPos,
    float3 center,
    float inner,
    float outer,
    float coverageScale,
    float3 wind,
    float4 layerPeaks,
    float4 layerWidths,
    float terrainHug,
    Texture3D weatherTex,
    out float extinction)
{
    extinction = 1.0;
    float lod = CloudWeatherLod(1.0);
    float4 weather = CloudSampleWeatherSharp(weatherTex, worldPos, center, inner, wind, lod);
    float surfaceH = CloudSampleSurface(weatherTex, worldPos, center);
    float h = CloudAltitude01(worldPos, center, inner, outer);
    float d = CloudFieldHeightOnly(worldPos, center, h, coverageScale, LayerConvective, weather.r,
        false, weather, terrainHug, surfaceH);
    d = max(d, CloudFieldHeightOnly(worldPos, center, h, coverageScale, LayerCirrus, weather.a,
        true, weather, terrainHug, surfaceH));
    extinction = lerp(0.55, 1.0, max(weather.r, weather.a));
    return saturate(d);
}

float CloudDensityFromBase(
    float baseDensity,
    float3 worldPos,
    float3 center,
    float volumeSize,
    float sampleStep,
    float inner,
    float3 wind,
    float4 weather,
    Texture3D detailTex)
{
    if (baseDensity <= 1e-5)
        return 0.0;
    if (volumeSize < 0.0 || inner < 0.0)
        return 0.0;
    float3 warp = (weather.bga - 0.5) * 0.08;
    float detailSize = CloudDetailLodSize();
    float detailLod = CloudNoiseLod(length(worldPos), sampleStep, detailSize, 64.0, 6.0);
    float4 d = CloudSampleShellTex(detailTex, worldPos, center, detailSize, wind * 1.85,
        (weather.bga - 0.5) * 0.45 + warp, 1.0, detailLod);
    // Nubis erosion: high-frequency Worley is the remap floor. ~0.2 keeps
    // the core; the edge (low base) and the crown lose more.
    float hf = saturate(d.r * 0.625 + d.g * 0.250 + d.b * 0.125);
    float edge = saturate(1.0 - baseDensity);
    float h = CloudReferenceHeight(worldPos, center, CenterInner.w);
    float crown = saturate((h - saturate(weather.g)) * 2.2);
    float erosion = CloudErosionStrength >= 0.0 ? clamp(CloudErosionStrength, 0.0, 2.0) : 1.0;
    float k = lerp(0.10, 0.28, edge) * lerp(0.75, 1.15, crown) * erosion;
    return CloudRemap01(baseDensity, hf * k, 1.0);
}

#endif
