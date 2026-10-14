// helpers: CloudStudio.hlsli v9
// (fingerprint v2)
// Placed studio boxes in the live scene. IsolatedMix: rgb is premultiplied
// radiance, alpha is opacity. The camera studio stays on CloudVolumeStudio.

#define ANOMALY_PACK_SRV0_TYPE Texture3D
#define ANOMALY_PACK_SRV1_TYPE Texture3D
#define ANOMALY_PACK_SRV3_TYPE StructuredBuffer<float4>
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudRay.hlsli>
#include <Clouds/CloudStudio.hlsli>

#define ShapeTex AnomalyPackSrv0
#define DetailTex AnomalyPackSrv1
#define Boxes AnomalyPackSrv3

#define STUDIO_WORLD_MAX 8

float StudioWorldSceneT(float2 pixel)
{
    float lin = AnomalyLinearDepth.SampleLevel(AnomalyPointSampler, AnomalyLightingUv(pixel), 0);
    if (!AnomalyIsForeground(lin, 2e7))
        return 2e7;
    return max(length(AnomalyLightingViewPos(pixel, lin)), 1.0);
}

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    int mode = (int)(StudioDebugMode + 0.5);
    bool debugField = mode == STUDIO_DEBUG_PROFILE || mode == STUDIO_DEBUG_SHAPE
        || mode == STUDIO_DEBUG_BASE || mode == STUDIO_DEBUG_ERODED;
    float sceneT = StudioWorldSceneT(pos.xy);
    float3 accum = 0.0;
    float through = 1.0;

    [loop]
    for (int i = 0; i < STUDIO_WORLD_MAX; i++)
    {
        float4 ax = Boxes[i * 4 + 1];
        if (ax.w < 1.0)
            continue;
        float4 ay = Boxes[i * 4 + 2];
        float4 az = Boxes[i * 4 + 3];
        float4 c = Boxes[i * 4];
        StudioAxisX = ax.xyz;
        StudioAxisY = ay.xyz;
        StudioAxisZ = az.xyz;
        float3 halfExt = float3(ax.w, ay.w, az.w);
        float4 layer = StudioMarch(pos.xy, uv, c.xyz, halfExt, sceneT, ShapeTex, DetailTex);
        if (debugField)
        {
            if (dot(layer.rgb, 1.0) < 1e-3)
                continue;
            accum += through * layer.rgb;
            through = 0.0;
            break;
        }
        accum += through * max(layer.rgb, 0.0);
        through *= saturate(layer.a);
        if (through < 0.02)
            break;
    }

    if (!all(isfinite(accum)) || !isfinite(through))
        return float4(0.0, 0.0, 0.0, 0.0);
    return float4(max(accum, 0.0), saturate(1.0 - through));
}
