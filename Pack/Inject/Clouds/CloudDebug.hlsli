// helpers: CloudDebug.hlsli v3
// IsolatedMix false-color. Packed in StepsFade.w = lightSteps + mode*16.
// Keep in sync with ClientPlugin.CloudDebugView.
#ifndef CLOUD_DEBUG_HLSLI
#define CLOUD_DEBUG_HLSLI

#define CLOUD_DEBUG_PACK 16.0
#define CLOUD_DEBUG_OFF 0
#define CLOUD_DEBUG_WEATHER_COVERAGE 1
#define CLOUD_DEBUG_WEATHER_TYPE 2
#define CLOUD_DEBUG_WEATHER_UV 3
#define CLOUD_DEBUG_WEATHER_MAP 4
#define CLOUD_DEBUG_ALTITUDE 5
#define CLOUD_DEBUG_HEIGHT_PROFILE 6
#define CLOUD_DEBUG_COVERAGE_AMOUNT 7
#define CLOUD_DEBUG_SHAPE_NOISE 8
#define CLOUD_DEBUG_DENSITY_BASE 9
#define CLOUD_DEBUG_DENSITY_ERODED 10
#define CLOUD_DEBUG_STORM_BODY 11
#define CLOUD_DEBUG_MARCH_STEPS 13
#define CLOUD_DEBUG_MARCH_COARSE_FINE 14
#define CLOUD_DEBUG_MARCH_EMPTY 15
#define CLOUD_DEBUG_TRANSMITTANCE 16
#define CLOUD_DEBUG_SAMPLE_POS 17
#define CLOUD_DEBUG_VIEW_LOD 18
#define CLOUD_DEBUG_MAX 18

int CloudDebugMode()
{
    return clamp((int)floor(StepsFade.w / CLOUD_DEBUG_PACK + 0.1), 0, CLOUD_DEBUG_MAX);
}

float CloudDebugLightSteps()
{
    int mode = CloudDebugMode();
    return clamp(StepsFade.w - (float)mode * CLOUD_DEBUG_PACK, 1.0, 6.0);
}

bool CloudDebugIsMarch(int mode)
{
    return mode >= CLOUD_DEBUG_MARCH_STEPS && mode <= CLOUD_DEBUG_TRANSMITTANCE;
}

bool CloudDebugIsSpatial(int mode)
{
    if (mode == CLOUD_DEBUG_STORM_BODY)
        return false;
    return mode > CLOUD_DEBUG_OFF && !CloudDebugIsMarch(mode);
}

float3 CloudDebugRamp(float t)
{
    t = saturate(t);
    float3 a = float3(0.03, 0.05, 0.18);
    float3 b = float3(0.08, 0.78, 0.42);
    float3 c = float3(0.96, 0.82, 0.12);
    float3 d = float3(0.92, 0.12, 0.06);
    if (t < 0.33)
        return lerp(a, b, t / 0.33);
    if (t < 0.66)
        return lerp(b, c, (t - 0.33) / 0.33);
    return lerp(c, d, (t - 0.66) / 0.34);
}

float3 CloudDebugMiss()
{
    return float3(0.04, 0.04, 0.05);
}

bool CloudDebugMapInset(float2 uv, Texture3D weatherTex, out float3 rgb)
{
    rgb = 0;
    // Top-right 2D weather map (D3D uv.y = 0 at top).
    float2 p0 = float2(0.70, 0.04);
    float2 p1 = float2(0.96, 0.30);
    if (uv.x < p0.x || uv.x > p1.x || uv.y < p0.y || uv.y > p1.y)
        return false;
    float2 local = (uv - p0) / max(p1 - p0, 1e-4);
    float border = min(min(local.x, local.y), min(1.0 - local.x, 1.0 - local.y));
    if (border < 0.03)
    {
        rgb = 0.02;
        return true;
    }
    float4 w = weatherTex.SampleLevel(AnomalyWrapSampler, float3(local, 0.25), 0.0);
    rgb = w.rgb;
    return true;
}

bool CloudDebugShellHit(float3 origin, float3 dir, float3 center, float inner, float outer,
    float t0, float t1, out float3 pos, out float tHit)
{
    pos = 0;
    tHit = -1.0;
    float hill = CloudHillRadius(inner);
    float airTop = CloudVisualCeil(hill);
    float r = lerp(hill, max(airTop, hill + 200.0), 0.42);
    float2 hit = CloudRaySphere(origin, dir, center, r);
    if (hit.x >= t0 && hit.x <= t1)
        tHit = hit.x;
    else if (hit.y >= t0 && hit.y <= t1)
        tHit = hit.y;
    else if (t1 > t0)
        tHit = lerp(t0, t1, 0.32);
    if (tHit < 0.0)
        return false;
    pos = origin + dir * tHit;
    return true;
}

float3 CloudDebugSpatial(int mode, float3 pos, float2 uv, float3 center, float inner, float outer,
    float coverageScale, float volumeSize, float3 wind, float cirrusStrength,
    Texture3D shapeTex, Texture3D detailTex, Texture3D weatherTex)
{
    if (mode == CLOUD_DEBUG_WEATHER_MAP)
    {
        float3 inset;
        if (CloudDebugMapInset(uv, weatherTex, inset))
            return inset;
    }
    if (mode == CLOUD_DEBUG_VIEW_LOD)
        return CloudDebugRamp(CloudViewLod());

    float4 weather = CloudSampleWeatherSharp(weatherTex, pos, center, inner, wind, 0.0);
    float surfaceH = CloudSampleSurface(weatherTex, pos, center);
    float h = CloudAltitude01(pos, center, inner, outer);
    float heightOnly = CloudFieldHeightOnly(pos, center, h, coverageScale, LayerConvective,
        weather.r, false, weather, TerrainHug, surfaceH);
    heightOnly = max(heightOnly, CloudFieldHeightOnly(pos, center, h, coverageScale, LayerCirrus,
        weather.a, true, weather, TerrainHug, surfaceH));
    float amount = CloudWeatherAmount(CloudWeatherMask(weather.r), CloudWeatherFill(coverageScale));
    float3 wuv = CloudWeatherUv(pos, center, max(WeatherScale, 1.0), wind, 0.0);
    float zStretch = 1.0;
    float size = CloudShapeLodSize(volumeSize);
    float4 shape = CloudSampleShape(shapeTex, pos, center, size, 40.0, wind, weather, zStretch);
    float4 weatherEval;
    float base = CloudBaseFromWeather(pos, center, inner, outer, coverageScale, volumeSize, 40.0,
        wind, cirrusStrength, 0, 0, TerrainHug, shapeTex, weatherTex, weatherEval);
    float eroded = CloudDensityFromBase(base, pos, center, volumeSize, 40.0, inner, wind,
        weatherEval, detailTex);
    float3 samplePos = CloudNubisSamplePos(pos, center, zStretch);

    if (mode == CLOUD_DEBUG_WEATHER_COVERAGE)
        return CloudDebugRamp(weather.r);
    if (mode == CLOUD_DEBUG_WEATHER_TYPE)
        return saturate(float3(weather.a, weather.g, weather.b));
    if (mode == CLOUD_DEBUG_WEATHER_UV)
    {
        // frac(u) is 0 on one side of the date line and 1 on the other, so it
        // paints a cut even though the climate sample wraps. cos/sin agree.
        float ang = wuv.x * 6.2831853;
        float2 grid = abs(frac(wuv.xy * 64.0) - 0.5);
        float gridLine = saturate(1.0 - min(grid.x, grid.y) * 32.0);
        float3 uvColor = float3(cos(ang) * 0.5 + 0.5, frac(wuv.y), sin(ang) * 0.5 + 0.5);
        return lerp(uvColor, float3(1, 1, 0.2), gridLine * 0.55);
    }
    if (mode == CLOUD_DEBUG_WEATHER_MAP)
        return CloudDebugRamp(weather.r);
    if (mode == CLOUD_DEBUG_ALTITUDE)
        return CloudDebugRamp(h);
    if (mode == CLOUD_DEBUG_HEIGHT_PROFILE)
        return CloudDebugRamp(heightOnly);
    if (mode == CLOUD_DEBUG_COVERAGE_AMOUNT)
        return CloudDebugRamp(amount);
    if (mode == CLOUD_DEBUG_SHAPE_NOISE)
    {
        // Nubis base: Perlin-Worley remapped by the Worley FBM floor.
        // Raw R sits high and reads as a flat wrap.
        float fbm = saturate(shape.g * 0.625 + shape.b * 0.250 + shape.a * 0.125);
        return CloudDebugRamp(CloudRemap01(saturate(shape.r), fbm * 0.55, 1.0));
    }
    if (mode == CLOUD_DEBUG_DENSITY_BASE)
        return CloudDebugRamp(saturate(base * 2.0));
    if (mode == CLOUD_DEBUG_DENSITY_ERODED)
        return CloudDebugRamp(saturate(eroded * 2.0));
    if (mode == CLOUD_DEBUG_SAMPLE_POS)
        return frac(abs(samplePos) / max(size, 1.0));
    return CloudDebugMiss();
}

float3 CloudDebugMarchColor(int mode, int steps, int coarse, int fine, int emptyExits,
    float transmittance, int stepLimit)
{
    if (mode == CLOUD_DEBUG_MARCH_STEPS)
        return CloudDebugRamp((float)steps / max((float)stepLimit, 1.0));
    if (mode == CLOUD_DEBUG_MARCH_COARSE_FINE)
    {
        float c = saturate((float)coarse / max((float)stepLimit, 1.0));
        float f = saturate((float)fine / max((float)stepLimit, 1.0));
        return float3(c, f, 0.08);
    }
    if (mode == CLOUD_DEBUG_MARCH_EMPTY)
        return CloudDebugRamp(saturate((float)emptyExits / 8.0));
    if (mode == CLOUD_DEBUG_TRANSMITTANCE)
        return CloudDebugRamp(1.0 - saturate(transmittance));
    return CloudDebugMiss();
}

#endif
