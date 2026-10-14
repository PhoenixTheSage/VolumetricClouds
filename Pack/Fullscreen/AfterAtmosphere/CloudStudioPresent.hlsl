// View-locked studio frame. History stays on the cloud layer; sky is this frame only.

#define ANOMALY_PACK_SRV0_TYPE Texture2D
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudRay.hlsli>
#include <Clouds/CloudStudio.hlsli>

#define CloudTex AnomalyPackSrv0

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float4 cloud = CloudTex.SampleLevel(AnomalyPointSampler, uv, 0);
    return StudioPresent(uv, cloud);
}
