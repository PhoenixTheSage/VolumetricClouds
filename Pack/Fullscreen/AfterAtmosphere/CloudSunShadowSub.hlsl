// AfterAtmosphere IsolatedSub. Catalog volumeSunShadow (Slice AN).
// dest*(1-src). Missing stamp (.a < 0.5) fail closed — no dest punch.
// Do not bind Keen CSM.

#include <AnomalyFullscreen.hlsli>

#define ShadowTex AnomalyPackSrv0

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float3 dest = AnomalySceneColor.SampleLevel(AnomalyLinearSampler, uv, 0).rgb;
    float remain = AnomalyVolumeSunShadow(ShadowTex, AnomalyLinearSampler, uv);
    float occ = saturate(1.0 - remain) * 0.62;
    return AnomalyIsolatedSubEnergy(dest * occ, dest);
}
