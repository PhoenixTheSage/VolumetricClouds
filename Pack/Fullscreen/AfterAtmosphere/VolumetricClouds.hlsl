// helpers: CloudDensity.hlsli v11 CloudLighting.hlsli v9 (fingerprint v12)
// IsolatedMix: rgb LBuffer energy. Shell stays inside AtmosphereRadius.
// Sun × AnomalySunVisibility. Night fill is AnomalyVolumeAmbient (extras).

#define ANOMALY_PACK_SRV0_TYPE Texture3D
#define ANOMALY_PACK_SRV1_TYPE Texture3D
#define ANOMALY_PACK_SRV2_TYPE Texture3D
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudDensity.hlsli>
#include <Clouds/CloudLighting.hlsli>

#define CenterInner AnomalyPassUniform0
#define SunOuter AnomalyPassUniform1
#define AlbedoCoverage AnomalyPassUniform2
#define WindDensity AnomalyPassUniform3
#define StepsFade AnomalyPassUniform4
#define VolumeHdr AnomalyPassUniform5
#define PlanetUp AnomalyPassUniform6.xyz
#define CamToShell AnomalyPassUniform6.w
#define ShapeTex AnomalyPackSrv0
#define DetailTex AnomalyPackSrv1
#define WeatherTex AnomalyPackSrv2

#define CLOUD_MAX_STEPS 28

float3 ViewToWorld(float3 view)
{
    return AnomalyCameraToWorld[0].xyz * view.x
         + AnomalyCameraToWorld[1].xyz * view.y
         + AnomalyCameraToWorld[2].xyz * view.z;
}

float2 RaySphere(float3 origin, float3 dir, float3 center, float radius)
{
    float3 oc = origin - center;
    float b = dot(oc, dir);
    float c = dot(oc, oc) - radius * radius;
    float disc = b * b - c;
    if (disc < 0)
        return float2(-1, -1);
    float s = sqrt(disc);
    return float2(-b - s, -b + s);
}

bool ShellInterval(float3 origin, float3 dir, float3 center, float inner, float outer, out float t0, out float t1)
{
    float2 oHit = RaySphere(origin, dir, center, outer);
    if (oHit.y < 0)
        return false;
    float2 iHit = RaySphere(origin, dir, center, inner);
    t0 = max(oHit.x, 0.0);
    t1 = oHit.y;
    if (iHit.x >= 0.0)
        t1 = min(t1, iHit.x);
    else if (iHit.y > t0)
        t0 = max(t0, iHit.y);
    return t1 > t0;
}

float HashIgn(float2 p)
{
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

void WriteColor(out float4 output, float3 color, float alpha)
{
    if (!all(isfinite(color)) || !isfinite(alpha))
    {
        output = 0;
        return;
    }
    output = float4(max(color, 0.0), saturate(alpha));
}

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float4 output;
    float3 center = CenterInner.xyz;
    float inner = CenterInner.w;
    float3 sunToward = AnomalySunToward;
    if (dot(sunToward, sunToward) < 1e-6)
        sunToward = SunOuter.xyz;
    if (dot(sunToward, sunToward) < 1e-6)
        sunToward = float3(0, 1, 0);
    else
        sunToward = normalize(sunToward);
    float outer = SunOuter.w;
    float3 albedo = AlbedoCoverage.xyz;
    float coverage = AlbedoCoverage.w;
    float3 wind = WindDensity.xyz;
    float densityMul = WindDensity.w;
    float stepBudget = StepsFade.x;
    float fade = StepsFade.y;
    float cirrus = StepsFade.z;
    float volumeSize = VolumeHdr.x;
    float hdrLift = VolumeHdr.y;
    float hillRadius = VolumeHdr.z > 1.0 ? VolumeHdr.z : inner;
    float atmoRadius = VolumeHdr.w > hillRadius ? VolumeHdr.w : hillRadius * 1.12;
    float atmoCeil = atmoRadius * 0.90;
    outer = min(outer, atmoCeil);
    inner = min(inner, outer - 40.0);
    float3 up = PlanetUp;
    if (dot(up, up) < 1e-4)
        up = float3(0, 1, 0);
    else
        up = normalize(up);

    if (fade <= 1e-4 || outer <= inner)
    {
        WriteColor(output, 0, 0);
        return output;
    }

    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    float2 scale = max(AnomalyProjScale, 1e-6);
    float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1);
    float3 rayDir = normalize(ViewToWorld(viewRay));
    float3 origin = 0;

    float viewZ = AnomalyLinearDepth.SampleLevel(AnomalyPointSampler, uv, 0);
    float sceneDist = viewZ > 0 ? length(viewRay) * viewZ : 1e7;

    float t0, t1;
    if (!ShellInterval(origin, rayDir, center, inner, outer, t0, t1))
    {
        WriteColor(output, 0, 0);
        return output;
    }
    t1 = min(t1, sceneDist);
    if (t1 <= t0)
    {
        WriteColor(output, 0, 0);
        return output;
    }

    float shellThickness = max(outer - inner, 1.0);
    float marchLength = min(t1 - t0, shellThickness * 4.0);
    t1 = t0 + marchLength;

    int steps = AnomalyMarchSteps(stepBudget, 12, CLOUD_MAX_STEPS,
        CamToShell, 0.0, max(shellThickness * 2.0, volumeSize));
    int lengthSteps = (int)ceil(marchLength / 280.0);
    steps = clamp(max(steps, lengthSteps), 12, CLOUD_MAX_STEPS);

    float jitter = HashIgn(pos.xy + float2(AnomalyLightingFrameIndex * 0.618, 17.13));
    float dt = marchLength / float(steps);
    float3 accum = 0;
    float transmittance = 1.0;

    float3 sunCol = AnomalySunColor * max(AnomalySunDiffuse, 1.0);
    sunCol *= hdrLift;
    float3 sky = AnomalyVolumeAmbient();
    float wrap = max(atmoRadius - hillRadius, hillRadius * 0.06);

    [loop]
    for (int i = 0; i < CLOUD_MAX_STEPS; i++)
    {
        if (i >= steps || transmittance < 0.02)
            break;
        float t = t0 + (float(i) + jitter) * dt;
        if (t > t1)
            break;
        float3 samplePos = origin + rayDir * t;
        float4 weather;
        float base = CloudBaseFromWeather(samplePos, center, inner, outer, coverage,
            volumeSize, wind, up, cirrus, ShapeTex, WeatherTex, weather);
        float density = CloudDensityFromBase(base, samplePos, center, volumeSize, inner,
            wind, weather, DetailTex);
        float entry = CloudRemap01(t, 80.0, 480.0);
        float radial = length(samplePos - center);
        float atmoFade = 1.0 - CloudRemap01(radial, atmoCeil * 0.92, atmoCeil);
        float sunVis = AnomalySunVisibility(samplePos, center, hillRadius, wrap);
        density *= densityMul * entry * atmoFade;
        if (density <= 1e-5)
            continue;

        float h = CloudAltitude01(samplePos, center, inner, outer);
        float3 radiance = CloudLitRadiance(albedo, sunCol, sky, h, density,
            dot(rayDir, sunToward), sunVis);
        float weight = CloudSegmentInscatter(density, dt);
        accum += transmittance * radiance * weight;
        transmittance *= CloudSegmentTransmittance(density, dt);
    }

    WriteColor(output, accum * fade, (1.0 - max(transmittance, 0.04)) * fade);
    return output;
}
