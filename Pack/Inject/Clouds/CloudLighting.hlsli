// helpers: CloudLighting.hlsli v11 — HZD beer-powder; Slice AM ambient is pre-scaled
#ifndef CLOUD_LIGHTING_HLSLI
#define CLOUD_LIGHTING_HLSLI

#define CLOUD_EXTINCTION 0.0022
#define CLOUD_MAX_OPTICAL_STEP 220.0

float CloudSegmentTransmittance(float density, float stepLen)
{
    float opticalLen = min(max(stepLen, 0.0), CLOUD_MAX_OPTICAL_STEP);
    return exp(-density * opticalLen * CLOUD_EXTINCTION);
}

float CloudSegmentInscatter(float density, float stepLen)
{
    return 1.0 - CloudSegmentTransmittance(density, stepLen);
}

// Cheap HG-like forward lobe without 4π (that factor made HDR sun look black).
float CloudPhase(float cosTheta)
{
    return lerp(0.62, 1.35, saturate(cosTheta * 0.5 + 0.5));
}

float CloudAnalyticLightOd(float h, float density, float sunVis)
{
    // Bases thicker toward the planet; night / grazing extra.
    float heightOd = (1.0 - h) * lerp(1.6, 0.45, saturate(sunVis));
    return heightOd * lerp(0.55, 1.15, saturate(density));
}

float3 CloudLitRadiance(float3 albedo, float3 sunCol, float3 sky, float h, float density,
    float cosViewSun, float sunVis)
{
    float vis = saturate(sunVis);
    float od = CloudAnalyticLightOd(h, density, vis);
    float beer = exp(-od);
    float powder = 1.0 - beer * beer;
    float3 sunLit = sunCol * albedo * beer * lerp(0.78, 1.12, powder) * CloudPhase(cosViewSun) * vis;
    // Slice AM: sky is AnomalyVolumeNight (already albedo-scaled). Do not
    // multiply albedo again. Dest luma is not an illuminant. Height still
    // lifts thin high decks.
    float3 ambient = sky * lerp(0.88, 1.06, h * h);
    return sunLit + ambient;
}

#endif
