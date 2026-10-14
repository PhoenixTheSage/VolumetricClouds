// Temporal blend of the studio march. Output is radiance and transmittance.
// Sky is composited by CloudStudioPresent.

#define ANOMALY_PACK_SRV0_TYPE Texture2D
#define ANOMALY_PACK_SRV3_TYPE Texture2D
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudTemporal.hlsli>

#define RawTex AnomalyPackSrv0
#define HistTex AnomalyPackSrv3

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    if (AnomalyPassUniform0.w > 0.5)
        return RawTex.SampleLevel(AnomalyPointSampler, uv, 0);
    return CloudTemporalBlend(RawTex, HistTex, AnomalyPointSampler, AnomalyLinearSampler,
        uv, uv, true);
}
