// Shared funnel radius. Shape 0 rope, 1 cone, 2 stovepipe, 3 wedge.
// t is 0 at the base and 1 at the top. Do not retune this for the studio.
#ifndef CLOUD_FUNNEL_HLSLI
#define CLOUD_FUNNEL_HLSLI

float CloudFunnelProfile(float shape, float t, float baseR)
{
    if (shape < 0.5)
        return baseR;
    if (shape < 1.5)
        return lerp(baseR * 0.35, baseR, t);
    if (shape < 2.5)
        return baseR;
    return lerp(baseR, baseR * 0.35, t);
}

float CloudStormTube(float horiz, float radius)
{
    float u = 1.0 - saturate(horiz / max(radius, 1.0));
    return u * u;
}

#endif
