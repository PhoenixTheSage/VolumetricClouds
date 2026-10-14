// Analytic storm body v4. Eight records on AnomalyPackSrv3 (t12), four float4s each.
// Stage: 1 supercell, 2 mesocyclone, 3 funnel aloft, 4 on the ground, 5 rope-out.
#ifndef CLOUD_STORM_HLSLI
#define CLOUD_STORM_HLSLI

#define CLOUD_STORM_COUNT 8
#define CLOUD_STORM_ANVIL 40000.0
#define CLOUD_STORM_FUNNEL 2000.0

float4 CloudStormRow(int index, int row)
{
    return AnomalyPackSrv3[index * 4 + row];
}

float CloudStormStage(int index)
{
    return CloudStormRow(index, 0).w;
}

float3 CloudStormOrigin(int index)
{
    return CloudStormRow(index, 0).xyz;
}

bool CloudStormNearFunnel(float3 samplePos)
{
    [unroll]
    for (int i = 0; i < CLOUD_STORM_COUNT; i++)
    {
        float stage = CloudStormStage(i);
        if (stage < 2.5)
            continue;
        if (length(samplePos - CloudStormOrigin(i)) < CLOUD_STORM_FUNNEL)
            return true;
    }
    return false;
}

float CloudStormGroundRadius(float3 origin, float3 center, Texture3D weatherTex)
{
    float surfaceH = CloudSampleSurface(weatherTex, origin, center);
    float sea = CloudSeaOrTerrain(CenterInner.w);
    float airTop = CloudVisualCeil(CloudHillRadius(CenterInner.w));
    return lerp(sea, max(airTop, sea + 200.0), saturate(surfaceH));
}

#include "CloudFunnel.hlsli"

float CloudStormOne(int index, float3 samplePos, float3 center, Texture3D weatherTex)
{
    float3 origin = CloudStormOrigin(index);
    float dist = length(samplePos - origin);
    if (dist > CLOUD_STORM_ANVIL)
        return 0.0;
    float stage = CloudStormStage(index);
    float4 b = CloudStormRow(index, 1);
    float4 c = CloudStormRow(index, 2);
    float intensity = saturate(b.x);
    if (intensity < 0.05)
        return 0.0;
    if (stage < 0.5 && dist > 25000.0)
        return 0.0;
    float shape = b.y;
    float2 sub = b.zw;
    float baseR = max(c.x, 40.0);
    float stageAge = c.y;
    float3 up = origin - center;
    float upLen = length(up);
    if (upLen < 1.0)
        return 0.0;
    up /= upLen;

    float3 fromCenter = samplePos - center;
    float along = dot(fromCenter, up);
    float bottom = CloudStormGroundRadius(origin, center, weatherTex);
    float narrow = 1.0;
    if (stage > 4.5)
    {
        float rope = saturate(stageAge / 20.0);
        bottom += rope * 1500.0;
        narrow = lerp(1.0, 0.15, rope);
    }
    float above = along - bottom;
    float3 onAxis = center + up * along;
    float3 off = samplePos - onAxis;
    float horiz = length(off);

    float3 east = cross(up, float3(0.0, 1.0, 0.0));
    if (dot(east, east) < 1e-4)
        east = float3(1.0, 0.0, 0.0);
    east = normalize(east);
    float3 north = normalize(cross(up, east));
    float s, co;
    sincos(stageAge * 0.7, s, co);
    float3 side = east * co + north * s;
    float3 side2 = east * (-s) + north * co;
    float hx = dot(off, side);
    float hy = dot(off, side2);

    float density = 0.0;
    float reach = 1.0 - saturate(dist / CLOUD_STORM_ANVIL);
    if (above > 1800.0 && above < 4500.0)
    {
        float slab = 1.0 - saturate(horiz / 12000.0);
        density = max(density, intensity * 0.45 * slab * slab * reach);
        float hook = length(float2(hx - 4000.0, hy));
        float hookD = 1.0 - saturate(hook / 2500.0);
        if (above < 3200.0)
            density = max(density, intensity * 0.35 * hookD * hookD * reach);
    }

    if (dist < CLOUD_STORM_FUNNEL && above > -20.0 && above < 1800.0)
    {
        float hail = 1.0 - saturate(horiz / 600.0);
        density = max(density, intensity * 0.55 * hail * hail);
        float rain = 1.0 - saturate(length(float2(hx + 3000.0, hy)) / 1400.0);
        density = max(density, intensity * 0.28 * rain * rain);
        float4 bolt = CloudStormRow(index, 3);
        if (bolt.x >= 0.0 && bolt.x < 0.15)
        {
            float wobble = sin(above * 0.02 + bolt.y) * 40.0;
            float boltD = 1.0 - saturate(length(float2(hx - wobble, hy)) / 25.0);
            density = max(density, boltD * boltD);
        }
    }

    if (stage < 2.5 || dist > CLOUD_STORM_FUNNEL)
        return density;

    float t = saturate(above / 2500.0);
    float rad = CloudFunnelProfile(shape, t, baseR) * narrow;
    density = max(density, intensity * CloudStormTube(length(float2(hx, hy)), rad));
    density = max(density, intensity * 0.65 * CloudStormTube(length(float2(hx - sub.x, hy)), rad * 0.45));
    density = max(density, intensity * 0.65 * CloudStormTube(length(float2(hx, hy - sub.y)), rad * 0.45));
    if (stage > 3.5 && stage < 4.5 && above > -20.0 && above < 40.0)
    {
        float scourR = max(CloudFunnelProfile(shape, 0.0, baseR) * narrow * 6.0, 80.0);
        float scour = 1.0 - saturate(horiz / scourR);
        density = max(density, intensity * 0.55 * scour * scour);
    }
    return density;
}

float CloudStormDensity(float3 samplePos, float3 center, Texture3D weatherTex)
{
    int best = -1;
    int second = -1;
    float bestD = CLOUD_STORM_ANVIL;
    float secondD = CLOUD_STORM_ANVIL;
    [unroll]
    for (int i = 0; i < CLOUD_STORM_COUNT; i++)
    {
        if (CloudStormStage(i) < 0.5)
            continue;
        float d = length(samplePos - CloudStormOrigin(i));
        if (d >= CLOUD_STORM_ANVIL)
            continue;
        if (d < bestD)
        {
            second = best;
            secondD = bestD;
            best = i;
            bestD = d;
        }
        else if (d < secondD)
        {
            second = i;
            secondD = d;
        }
    }

    float density = 0.0;
    if (best >= 0)
        density = CloudStormOne(best, samplePos, center, weatherTex);
    if (second >= 0)
        density = max(density, CloudStormOne(second, samplePos, center, weatherTex));
    return saturate(density);
}

#endif
