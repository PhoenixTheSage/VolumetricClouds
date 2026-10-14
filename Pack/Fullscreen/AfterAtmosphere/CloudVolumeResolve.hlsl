// Planet temporal resolve. Raw alpha packs opacity and the first lit hit.
// Output alpha is opacity for IsolatedMix.

#define ANOMALY_PACK_SRV0_TYPE Texture2D
#define ANOMALY_PACK_SRV3_TYPE Texture2D
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudRay.hlsli>
#include <Clouds/CloudUniforms.hlsli>
#include <Clouds/CloudTemporal.hlsli>

#define RawTex AnomalyPackSrv0
#define HistTex AnomalyPackSrv3

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float4 raw = RawTex.SampleLevel(AnomalyPointSampler, uv, 0);
    int debugMode = clamp((int)floor(StepsFade.w / 16.0 + 0.1), 0, 18);
    if (debugMode != 0)
        return float4(max(raw.rgb, 0.0), saturate(raw.a));

    float opacity;
    float hit;
    CloudUnpackHit(raw.a, opacity, hit);
    float4 cur = float4(max(raw.rgb, 0.0), opacity);
    bool accept = false;
    float2 prevUv = uv;
    if (hit > 1.0 && AnomalyLightingHistoryValid != 0)
    {
        float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
        float2 scale = max(AnomalyProjScale, 1e-6);
        float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1.0);
        float3 rayDir = normalize(CloudViewToWorld(viewRay));
        float3 wind = WindDensity.xyz;
        float3 advect = -(wind - CloudPrevWind) * max(VolumeHdr.x, 1.0);
        accept = AnomalyPrevUv(rayDir * hit + advect, prevUv);
    }

    if (!accept || AnomalyLightingHistoryValid == 0)
        return cur;

    float2 invSize = max(AnomalyInvPassSize, 1e-6);
    float alo = opacity;
    float ahi = opacity;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float4 n = RawTex.SampleLevel(AnomalyPointSampler, uv + float2(x, y) * invSize, 0);
            float nOp, nHit;
            CloudUnpackHit(n.a, nOp, nHit);
            alo = min(alo, nOp);
            ahi = max(ahi, nOp);
        }
    }
    float4 hist = HistTex.SampleLevel(AnomalyLinearSampler, prevUv, 0);
    if (hist.a < 0.0)
        return cur;
    float3 lo = cur.rgb;
    float3 hi = cur.rgb;
    [unroll] for (int y2 = -1; y2 <= 1; y2++)
    {
        [unroll] for (int x2 = -1; x2 <= 1; x2++)
        {
            float3 n = RawTex.SampleLevel(AnomalyPointSampler, uv + float2(x2, y2) * invSize, 0).rgb;
            lo = min(lo, n);
            hi = max(hi, n);
        }
    }
    float3 hrgb = clamp(hist.rgb, lo, hi);
    float ha = clamp(hist.a, alo, ahi);
    return float4(lerp(hrgb, cur.rgb, 0.125), lerp(ha, cur.a, 0.125));
}
