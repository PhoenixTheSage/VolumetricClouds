// PublishOnly catalog volumeSunShadow. Occupied air-column weather OD.
// Sun vis is CloudSunPlanetR (sea/valleys). Dest hit is GBuffer only.
// .r = remaining sun. .a < 0.5 = missing (AnomalyVolumeSunShadow fail closed to 1).

#define ANOMALY_PACK_SRV0_TYPE Texture3D
#define ANOMALY_PACK_SRV1_TYPE Texture3D
#define ANOMALY_PACK_SRV2_TYPE Texture3D
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudUniforms.hlsli>
#include <Clouds/CloudRay.hlsli>
#include <Clouds/CloudDensity.hlsli>

#define ShapeTex AnomalyPackSrv0
#define WeatherTex AnomalyPackSrv2
#define CLOUD_SHADOW_STEPS 12

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float fade = StepsFade.y;
    if (fade <= 1e-4)
        return float4(1, 1, 1, 1);

    float3 center = CenterInner.xyz;
    float inner = CenterInner.w;
    float outer = SunOuter.w;
    inner = min(inner, outer - 40.0);
    float occInner, occOuter;
    CloudOccupiedRadii(inner, outer, occInner, occOuter);
    if (occOuter <= occInner)
        return float4(1, 1, 1, 1);

    float3 sunToward = AnomalySunToward;
    if (dot(sunToward, sunToward) < 1e-6)
        sunToward = SunOuter.xyz;
    if (dot(sunToward, sunToward) < 1e-8)
        return float4(1, 1, 1, 1);
    sunToward = normalize(sunToward);

    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    float2 scale = max(AnomalyProjScale, 1e-6);
    float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1);
    float3 rayDir = normalize(CloudViewToWorld(viewRay));
    float viewZ = AnomalyLinearDepth.SampleLevel(AnomalyPointSampler, uv, 0);
    float hillRadius = VolumeHdr.z > 1.0 ? VolumeHdr.z : inner;
    float airTop = AnomalyVolumeCeil();
    if (airTop < hillRadius + 40.0)
        airTop = VolumeHdr.w > hillRadius ? VolumeHdr.w : hillRadius * 1.12;

    // Sun vis uses sea/valleys. Dest hit is GBuffer only — occOuter as a
    // sky fallback is a spherical disk through IsolatedSub.
    float planetR = CloudSunPlanetR(inner);
    if (viewZ <= 0.0)
        return float4(1, 1, 1, 1);
    float tHit = length(viewRay) * viewZ;

    float3 samplePos = rayDir * tHit;
    float sunVis = AnomalySunTransmittance(samplePos, center, planetR, airTop);
    if (sunVis < 0.04)
        return float4(1, 1, 1, 1);

    float coverage = AlbedoCoverage.w;
    float3 wind = WindDensity.xyz;
    float densityMul = WindDensity.w;
    float2 aHit = CloudRaySphere(samplePos, sunToward, center, occOuter);
    if (aHit.y < 0.0)
        return float4(1, 1, 1, 1);
    float t0 = max(aHit.x, 0.0);
    float t1 = aHit.y;
    float2 iHit = CloudRaySphere(samplePos, sunToward, center, occInner);
    if (iHit.x >= 0.0 && iHit.y > t0)
        t1 = min(t1, iHit.x);
    if (t1 <= t0 + 1.0)
        return float4(1, 1, 1, 1);

    float march = min(t1 - t0, max(occOuter - occInner, 80.0) * 3.0);
    float dt = march / (float)CLOUD_SHADOW_STEPS;
    float od = 0.0;
    // Dynamic. The shell chart blend makes a forced unroll of 12 too large.
    for (int i = 0; i < CLOUD_SHADOW_STEPS; i++)
    {
        float3 p = samplePos + sunToward * (t0 + (float(i) + 0.5) * dt);
        float4 weather;
        float d = CloudBaseFromWeather(p, center, inner, outer, coverage,
            VolumeHdr.x, dt, wind, StepsFade.z, 0, 0, TerrainHug,
            ShapeTex, WeatherTex, weather);
        float ext = lerp(0.55, 1.0, max(weather.r, weather.a));
        d *= ext;
        od += d * densityMul * dt * 0.0022;
    }
    float remain = saturate(exp(-od));
    return float4(remain, remain, remain, 1.0);
}
