// helpers: CloudStudio.hlsli v9
// (fingerprint v9)
// Temporary live test. Replace the frame with a blue sky and one cumulus.
// Scene sun is AnomalySunColor / AnomalySunToward. The planet shell is not marched.

#define ANOMALY_PACK_SRV0_TYPE Texture3D
#define ANOMALY_PACK_SRV1_TYPE Texture3D
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudRay.hlsli>
#include <Clouds/CloudStudio.hlsli>

#define ShapeTex AnomalyPackSrv0
#define DetailTex AnomalyPackSrv1

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    return StudioShade(pos.xy, uv, ShapeTex, DetailTex);
}
