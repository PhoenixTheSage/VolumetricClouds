// helpers: CloudDensity.hlsli v15 — wrapping 3D weather; layer bands; terrain hug; band floor
#ifndef CLOUD_DENSITY_HLSLI
#define CLOUD_DENSITY_HLSLI

float CloudRemap01(float x, float a, float b)
{
    return saturate((x - a) / max(b - a, 1e-5));
}

float CloudAltitude01(float3 worldPos, float3 center, float inner, float outer)
{
    float r = length(worldPos - center);
    return saturate((r - inner) / max(outer - inner, 1e-3));
}

// 1 near the camera, 0 beyond ~20 km. Used for fine erosion, not to drop 3D shape.
float CloudNearWeight(float3 worldPos)
{
    return 1.0 - saturate((length(worldPos) - 800.0) / 20000.0);
}

// HZD / Skybolt: large structure is the weather field. 3D noise stays
// cloud-scale (hundreds of meters). Inflating cells with camera distance
// turns cumulus into one fog blob on the planet disk.
float CloudShapeSize(float3 worldPos, float volumeSize, float inner)
{
    return max(volumeSize, 1.0);
}

void CloudRotatedAxes(out float3 r1, out float3 r2, out float3 r3)
{
    r1 = normalize(float3(0.80, 0.60, 0.02));
    r2 = normalize(float3(-0.37, 0.48, 0.79));
    r3 = normalize(float3(0.47, -0.64, 0.61));
}

float3 CloudNoiseUv(float3 worldPos, float3 center, float volumeSize, float3 wind, float3 phase)
{
    float3 p = (worldPos - center) / max(volumeSize, 1.0);
    float3 r1, r2, r3;
    CloudRotatedAxes(r1, r2, r3);
    return float3(dot(p, r1), dot(p, r2), dot(p, r3)) + wind + phase;
}

// 2D lat/lon pinches at poles. 2D triplanar cuts the sphere on cube faces
// (great-circle seams in the orbit screenshots). A wrapping 3D field has
// neither: it is continuous on the shell.
float4 CloudSampleWeather(Texture3D weatherTex, float3 worldPos, float3 center, float inner, float3 wind)
{
    float3 q = CloudNoiseUv(worldPos, center, max(inner, 1.0) * 1.05, wind, 0.0);
    float4 w0 = weatherTex.SampleLevel(AnomalyWrapSampler, q, 0);
    float3 warp = (w0.gba - 0.5) * 0.045;
    float4 w1 = weatherTex.SampleLevel(AnomalyWrapSampler, q + warp, 0);
    return lerp(w0, w1, 0.42);
}

float CloudLayerBand(float h, float center, float width)
{
    if (width < 1e-4)
        return 0.0;
    float lo = center - width;
    float hi = center + width;
    return smoothstep(lo, center - width * 0.35, h)
        * (1.0 - smoothstep(center + width * 0.35, hi, h));
}

float CloudLayerBands(float h, float4 peaks, float4 widths)
{
    if (widths.x < 1e-4)
        return 1.0;
    float e = CloudLayerBand(h, peaks.x, widths.x);
    e = max(e, CloudLayerBand(h, peaks.y, widths.y));
    e = max(e, CloudLayerBand(h, peaks.z, widths.z));
    e = max(e, CloudLayerBand(h, peaks.w, widths.w));
    return saturate(e);
}

// Schneider / Nubis: stratus, cumulus, cumulonimbus as height profiles.
// type 0 = thin low deck, 1 = tall tower + anvil. Convection lifts the anvil.
// Layer peaks stack extra decks inside the allowed column. Inner wall is
// thin when the volume hugs terrain (GBuffer clips voxels).
float CloudHeightGradient(float h, float type, float convection,
    float4 peaks, float4 widths, float terrainHug)
{
    float t = saturate(type);
    float stratus = smoothstep(0.00, 0.06, h) * (1.0 - smoothstep(0.16, 0.30, h));
    float cumulus = smoothstep(0.02, 0.12, h) * (1.0 - smoothstep(0.40, 0.62, h));
    float nimbus = smoothstep(0.02, 0.10, h) * (1.0 - smoothstep(0.70, 0.94, h));
    float anvil = smoothstep(0.58, 0.74, h) * (1.0 - smoothstep(0.86, 1.0, h))
        * saturate(t * 1.35 - 0.35) * saturate(convection);
    float mid = lerp(cumulus, nimbus, saturate(t * 1.6 - 0.55));
    float body = lerp(stratus, mid, saturate(t * 1.25));
    float innerWall = CloudRemap01(h, 0.0, terrainHug > 0.5 ? 0.03 : 0.10);
    float outerWall = 1.0 - CloudRemap01(h, 0.88, 1.0);
    float bands = CloudLayerBands(h, peaks, widths);
    // Pertam layer peaks remap to a few thin 0–1 bands. Multiplying the
    // whole column by that punched holes (h 0.08–0.26 empty). Keep a floor
    // so weather still fills the allowed min/max shell.
    return saturate(body + anvil) * innerWall * outerWall * max(bands, 0.42);
}

float4 CloudSampleShape(Texture3D shapeTex, float3 worldPos, float3 center, float volumeSize,
    float3 wind, float4 weather)
{
    float3 phaseA = weather.gba * 6.13;
    float3 phaseB = weather.agb * 3.71 + 1.17;
    float4 a = shapeTex.SampleLevel(AnomalyWrapSampler,
        CloudNoiseUv(worldPos, center, volumeSize, wind, phaseA), 0);
    float4 b = shapeTex.SampleLevel(AnomalyWrapSampler,
        CloudNoiseUv(worldPos, center, volumeSize * 2.718, wind * 0.47, phaseB), 0);
    return lerp(a, b, 0.42);
}

float CloudBaseFromWeather(
    float3 worldPos,
    float3 center,
    float inner,
    float outer,
    float coverageScale,
    float volumeSize,
    float3 wind,
    float3 up,
    float cirrusStrength,
    float4 layerPeaks,
    float4 layerWidths,
    float terrainHug,
    Texture3D shapeTex,
    Texture3D weatherTex,
    out float4 weather)
{
    weather = CloudSampleWeather(weatherTex, worldPos, center, inner, wind);
    float h = CloudAltitude01(worldPos, center, inner, outer);
    float type = weather.g;
    // Planet-wide coverage from the weather map. Do not multiply by population
    // (that zeroed 3/4 of the globe into one storm island / pie-slice).
    float coverage = saturate(CloudRemap01(weather.r * coverageScale, 0.22, 0.90));
    float height = CloudHeightGradient(h, type, weather.a, layerPeaks, layerWidths, terrainHug);
    if (coverage * height < 1e-4 && cirrusStrength < 0.02)
        return 0.0;

    float base = 0.0;
    float shapeSize = CloudShapeSize(worldPos, volumeSize, inner);
    if (coverage * height > 1e-4)
    {
        float4 n = CloudSampleShape(shapeTex, worldPos, center, shapeSize, wind, weather);
        float shapeFbm = saturate(n.g * 0.50 + n.b * 0.31 + n.a * 0.19);
        float carveMix = 0.28 + type * 0.22;
        float cellularCarve = lerp(shapeFbm - 1.0, shapeFbm * 0.48 - 0.34, carveMix);
        float carved = CloudRemap01(n.r, cellularCarve, 1.0);
        float cellularAmount = min(0.52 + type * 0.22, 0.78);
        float base3d = lerp(n.r, carved, cellularAmount) * height;
        // Schneider: remap(noise, 1-coverage, 1) * coverage. Macro fill is fog.
        base = CloudRemap01(base3d, 1.0 - coverage, 1.0) * coverage;
    }

    float cirrusH = CloudRemap01(h, 0.40, 0.90) * (1.0 - CloudRemap01(h, 0.86, 1.0));
    float cirrusAmt = saturate(cirrusStrength) * cirrusH;
    if (cirrusAmt > 0.02)
    {
        float3 alongUp = up * dot(worldPos - center, up);
        float3 stretched = worldPos - alongUp * 0.62;
        float3 uvw = CloudNoiseUv(stretched, center, shapeSize * 1.85,
            wind * float3(1.65, 0.12, 1.65), weather.rga * 2.05);
        float4 cn = shapeTex.SampleLevel(AnomalyWrapSampler, uvw, 0);
        float veil = saturate(cn.g * 0.52 + cn.b * 0.48);
        veil = CloudRemap01(veil, 0.38, 0.80) * cirrusAmt * lerp(0.22, 0.85, weather.b);
        base = max(base, veil * 0.42);
    }

    return saturate(base);
}

float CloudDensityFromBase(
    float baseDensity,
    float3 worldPos,
    float3 center,
    float volumeSize,
    float inner,
    float3 wind,
    float4 weather,
    Texture3D detailTex)
{
    if (baseDensity <= 1e-5)
        return 0.0;
    float edge = saturate((0.48 - baseDensity) / 0.48);
    if (edge < 0.02)
        return baseDensity;
    float3 warp = (weather.bga - 0.5) * 0.08;
    float detailSize = CloudShapeSize(worldPos, volumeSize, inner) * 0.22;
    float3 uvw = CloudNoiseUv(worldPos, center, detailSize, wind * 1.85,
        weather.bga * 2.3 + warp);
    float4 d = detailTex.SampleLevel(AnomalyWrapSampler, uvw, 0);
    float erode = lerp(lerp(d.r, d.g, 0.48), d.b, 0.38);
    float carve = erode * lerp(0.18, 0.55, edge) * edge;
    return CloudRemap01(baseDensity, carve, 1.0);
}

#endif
