// helpers: CloudLighting.hlsli v18 — procedural cone lighting, no brick shading
#ifndef CLOUD_LIGHTING_HLSLI
#define CLOUD_LIGHTING_HLSLI

#define CLOUD_EXTINCTION 0.01
#define CLOUD_MAX_OPTICAL_STEP 220.0
#define CLOUD_LIGHT_MAX_STEPS 6

float CloudSegmentTransmittance(float density, float stepLen)
{
    float opticalLen = min(max(stepLen, 0.0), CLOUD_MAX_OPTICAL_STEP);
    return exp(-density * opticalLen * CLOUD_EXTINCTION);
}

float CloudSegmentInscatter(float density, float stepLen)
{
    return 1.0 - CloudSegmentTransmittance(density, stepLen);
}

float CloudPhase(float cosTheta)
{
    return lerp(0.62, 1.35, saturate(cosTheta * 0.5 + 0.5));
}

float CloudSunColumnOd(
    float3 samplePos,
    float3 center,
    float inner,
    float outer,
    float coverageScale,
    float volumeSize,
    float3 wind,
    float cirrusStrength,
    float terrainHug,
    float3 sunToward,
    float sunVis,
    float lightSteps,
    Texture3D shapeTex,
    Texture3D weatherTex,
    out float extinction)
{
    extinction = 1.0;
    if (sunVis < 0.04 || lightSteps < 0.5)
        return 0.0;

    int steps = clamp((int)(lightSteps + 0.5), 1, CLOUD_LIGHT_MAX_STEPS);
    float hill = CloudHillRadius(inner);
    float occInner, occOuter;
    CloudOccupiedRadii(inner, outer, occInner, occOuter);
    float column = max(occOuter - max(inner, hill), 80.0);
    float dt = min(max(column / (float)(steps + 1), 70.0), 220.0);
    float3 refAxis = abs(sunToward.y) < 0.92
        ? float3(0.0, 1.0, 0.0)
        : float3(1.0, 0.0, 0.0);
    float3 coneX = normalize(cross(sunToward, refAxis));
    float3 coneY = cross(coneX, sunToward);
    float angle0 = AnomalyIgnWorld(samplePos.xz) * 6.2831853;
    float travel = 0.0;
    float od = 0.0;
    [loop]
    for (int i = 1; i <= CLOUD_LIGHT_MAX_STEPS; i++)
    {
        if (i > steps)
            break;
        float fi = (float)i;
        float segment = dt * lerp(0.85, 1.35, fi / (float)steps);
        travel += segment;
        float angle = angle0 + fi * 2.3999632;
        float s, c;
        sincos(angle, s, c);
        float radius = travel * 0.10 * (fi / (float)steps);
        float3 p = samplePos + sunToward * travel + (coneX * c + coneY * s) * radius;
        float4 weather;
        float d = CloudBaseFromWeather(p, center, inner, outer, coverageScale,
            volumeSize, segment, wind, cirrusStrength, 0, 0, terrainHug,
            shapeTex, weatherTex, weather);
        float ext = lerp(0.55, 1.0, max(weather.r, weather.a));
        extinction = max(extinction, ext);
        od += d * segment * CLOUD_EXTINCTION * ext;
    }
    return od;
}

float3 CloudLitRadiance(float3 albedo, float3 sunCol, float3 sky, float h, float density,
    float cosViewSun, float sunVis, float odSun)
{
    float vis = saturate(sunVis);
    // Cap OD so underbelly is a shaded wrap, not a black Low/Mid slab.
    float beer = exp(-min(max(odSun, 0.0), 1.35));
    float powder = 1.0 - beer * beer;
    float3 sunLit = sunCol * albedo * beer * lerp(0.78, 1.12, powder) * CloudPhase(cosViewSun) * vis;
    float3 dayFill = AnomalyVolumeAmbient() * albedo;
    float3 wrap = lerp(sky, dayFill, vis) * lerp(0.62, 1.0, beer);
    return sunLit + wrap;
}

#endif
