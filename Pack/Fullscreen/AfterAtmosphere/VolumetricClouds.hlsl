// helpers: CloudDensity.hlsli v82 CloudLighting.hlsli v18 CloudDebug.hlsli v3 CloudStorm.hlsli v4
// (fingerprint v100)
// IsolatedMix is the Nubis ray-march (density → light → integrate).
// Debug views: CloudDebugMode in StepsFade.w (lightSteps + mode*16).

#ifndef CLOUD_DEBUG_SUN_VIS
#define CLOUD_DEBUG_SUN_VIS 0
#endif

#define ANOMALY_PACK_SRV0_TYPE Texture3D
#define ANOMALY_PACK_SRV1_TYPE Texture3D
#define ANOMALY_PACK_SRV2_TYPE Texture3D
#define ANOMALY_PACK_SRV3_TYPE StructuredBuffer<float4>
#include <AnomalyFullscreen.hlsli>
#include <Clouds/CloudUniforms.hlsli>
#include <Clouds/CloudRay.hlsli>
#include <Clouds/CloudDensity.hlsli>
#include <Clouds/CloudLighting.hlsli>
#include <Clouds/CloudDebug.hlsli>
#include <Clouds/CloudStorm.hlsli>
#include <Clouds/CloudTemporal.hlsli>

#define ShapeTex AnomalyPackSrv0
#define DetailTex AnomalyPackSrv1
#define WeatherTex AnomalyPackSrv2

#define CLOUD_MAX_STEPS 160
#define CLOUD_FINE_EMPTY_EXIT 10

bool ShellInterval(float3 origin, float3 dir, float3 center, float inner, float outer,
    float planetR, float sceneDist, float farCap, out float t0, out float t1)
{
    t0 = 0.0;
    t1 = -1.0;
    float2 oHit = CloudRaySphere(origin, dir, center, outer);
    if (oHit.y < 0)
        return false;
    t0 = max(oHit.x, 0.0);
    t1 = oHit.y;
    t1 = min(t1, sceneDist);
    // Skip only the solid planet (valley radius). Do not skip MaximumRadius —
    // that was the lower occluding sphere. GBuffer still clips the mesh.
    float3 oc = origin - center;
    if (dot(oc, oc) < inner * inner)
    {
        float2 skip = CloudRaySphere(origin, dir, center, inner);
        if (skip.y > t0)
            t0 = max(t0, skip.y);
    }
    // Far-cap fades in with view LOD. A binary orbit flag was a plane
    // through the column as soon as CamToShell opened.
    if (farCap > 1e-3)
    {
        float clipR = planetR + 80.0;
        if (clipR + 1.0 < outer)
        {
            float2 pHit = CloudRaySphere(origin, dir, center, clipR);
            if (pHit.x >= 0.0)
                t1 = lerp(t1, min(t1, pHit.x), farCap);
        }
    }
    float2 iHit = CloudRaySphere(origin, dir, center, inner);
    if (iHit.x >= 0.0)
        t1 = min(t1, iHit.x);
    else if (farCap > 1e-3)
    {
        float tClose = -dot(oc, dir);
        if (tClose > t0 + 1.0 && tClose < t1)
        {
            float rMin2 = dot(oc, oc) - tClose * tClose;
            if (rMin2 < outer * outer)
                t1 = lerp(t1, min(t1, tClose), farCap);
        }
    }
    return t1 > t0;
}

// GAP: Slice AU — a few-km deck on a ~60 km planet. Grazing rays follow the
// surface out to HorizonRange. Do not lerp a sky sample down onto the deck;
// that rolled pillars out of the ceiling into a fixed distance.
float3 CloudFlatDeckPos(float3 rayDir, float t, float3 center)
{
    float3 up = normalize(-center);
    float camR = max(length(center), 1.0);
    float elev = dot(rayDir, up);
    float3 horizontal = rayDir - up * elev;
    float horizLen = length(horizontal);
    if (horizLen < 1e-4)
        return rayDir * t;
    float3 axis = horizontal / horizLen;
    float angle = (t * horizLen) / camR;
    float3 dir = up * cos(angle) + axis * sin(angle);
    float radius = max(camR + t * elev, 1.0);
    return center + dir * radius;
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
    float cirrusStrength = StepsFade.z;
    int debugMode = CloudDebugMode();
    float lightSteps = CloudDebugLightSteps();
    float volumeSize = VolumeHdr.x;
    float hdrLift = VolumeHdr.y;
    float hillRadius = VolumeHdr.z > 1.0 ? VolumeHdr.z : inner;
    float atmoRadius = VolumeHdr.w > hillRadius ? VolumeHdr.w : hillRadius * 1.12;
    inner = min(inner, outer - 40.0);
    float occInner, occOuter;
    CloudOccupiedRadii(inner, outer, occInner, occOuter);
    if (fade <= 1e-4)
    {
        WriteColor(output, 0, 0);
        return output;
    }

    if (occOuter <= occInner)
    {
        WriteColor(output, 0, 0);
        return output;
    }

    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    float2 scale = max(AnomalyProjScale, 1e-6);
    float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1);
    float3 rayDir = normalize(CloudViewToWorld(viewRay));
    float3 origin = 0;

    float viewZ = AnomalyLinearDepth.SampleLevel(AnomalyPointSampler, uv, 0);
    float sceneDist = viewZ > 0 ? length(viewRay) * viewZ : 1e7;
    float viewLod = CloudViewLod();
    float farCap = smoothstep(0.25, 0.45, viewLod);

    float t0, t1;
    float solidR = CloudSunPlanetR(inner);
    // March the occupied shell. Coarse steps use the live weather×height profile.
    if (!ShellInterval(origin, rayDir, center, occInner, occOuter, solidR, sceneDist,
        farCap, t0, t1))
    {
        if (debugMode == CLOUD_DEBUG_VIEW_LOD)
        {
            WriteColor(output, CloudDebugRamp(viewLod), 1.0);
            return output;
        }
        float3 inset;
        if (debugMode == CLOUD_DEBUG_WEATHER_MAP && CloudDebugMapInset(uv, WeatherTex, inset))
        {
            WriteColor(output, inset, 1.0);
            return output;
        }
        WriteColor(output, 0, 0);
        return output;
    }
    float horizonT = max(CloudHorizonRange, 80.0);
    t1 = min(t1, lerp(horizonT, t1, viewLod));

    t0 = max(t0, max(CloudLegacyNearT, 0.0));
    if (t1 <= t0) { WriteColor(output, 0, 0); return output; }

    float lodInner = lerp(hillRadius, min(CloudColumnFloor(inner), hillRadius), viewLod);
    float shellThickness = max(occOuter - lodInner, 1.0);
    float maxChord = 2.0 * sqrt(max(occOuter * occOuter - lodInner * lodInner, 1.0));
    float marchLength = min(t1 - t0, max(maxChord, shellThickness * 4.0));
    t1 = t0 + marchLength;

    // Steep rays and orbit keep the geometric shell. Near the horizon, the
    // march runs out to HorizonRange on the surface so the slider is the
    // cutoff. Ceiling rays are not pulled down onto that line.
    float3 cameraUp = dot(center, center) > 1.0 ? normalize(-center) : float3(0, 1, 0);
    float grazing = 1.0 - abs(dot(rayDir, cameraUp));
    float deckFollow = (1.0 - viewLod) * smoothstep(0.72, 0.90, grazing);
    if (deckFollow > 0.5)
        t1 = min(horizonT, sceneDist);

    // Nubis uses 54–96 samples from vertical to horizon, with large cheap
    // search steps and short detailed integration steps. Quality is an upper
    // bound; empty air does not pay that full cost.
    int configuredSteps = clamp((int)(stepBudget + 0.5), 32, CLOUD_MAX_STEPS);
    int rayBudget = clamp((int)(lerp((float)configuredSteps * 0.72,
        (float)configuredSteps, grazing) + 0.5), 32, CLOUD_MAX_STEPS);
    int safetyFloor = max(32, (int)((float)rayBudget * 0.75));
    int stepLimit = AnomalyMarchSteps((float)rayBudget, safetyFloor, CLOUD_MAX_STEPS,
        CamToShell, 0.0, max(shellThickness * 2.0, volumeSize));
    int farLimit = max(32, (int)((float)stepLimit * 0.65 + 0.5));
    stepLimit = (int)(lerp((float)stepLimit, (float)farLimit, viewLod) + 0.5);
    // Nubis Evolved / Cubed near step. Coarse search (below) covers empty
    // air; stepLimit only caps fine integration inside cloud. Spreading the
    // chord across the budget forced 100–800 m steps and mip-wiped the uprez.
    float fineBase = CloudSampleSpacing > 1.0
        ? clamp(CloudSampleSpacing, 20.0, 120.0)
        : 40.0;
    float jitter = AnomalyIgnFrame(pos.xy);
    float t = t0 + jitter * fineBase;
    float hitT = -1.0;
    float3 accum = 0;
    float stormVis = 0.0;
    float transmittance = 1.0;
    bool fineMarch = false;
    int emptyFine = 0;
    int marchSteps = 0;
    int marchCoarse = 0;
    int marchFine = 0;
    int marchEmptyExits = 0;

    float3 sunCol = AnomalySunColor * max(AnomalySunDiffuse, 1.0);
    float airTop = AnomalyVolumeCeil();
    if (airTop < hillRadius + 40.0)
        airTop = atmoRadius;

#if CLOUD_DEBUG_SUN_VIS
    {
        float3 p = origin + rayDir * t0;
        float v = AnomalySunTransmittance(p, center, CloudSunPlanetR(inner), airTop);
        WriteColor(output, v.xxx, 1.0);
        return output;
    }
#endif

    if (debugMode == CLOUD_DEBUG_VIEW_LOD)
    {
        WriteColor(output, CloudDebugRamp(viewLod), 1.0);
        return output;
    }
    if (CloudDebugIsSpatial(debugMode))
    {
        float3 inset;
        if (debugMode == CLOUD_DEBUG_WEATHER_MAP && CloudDebugMapInset(uv, WeatherTex, inset))
        {
            WriteColor(output, inset, 1.0);
            return output;
        }
        float3 shellPos;
        float shellT;
        if (CloudDebugShellHit(origin, rayDir, center, inner, outer, t0, t1, shellPos, shellT))
        {
            float3 rgb = CloudDebugSpatial(debugMode, shellPos, uv, center, inner, outer,
                coverage, volumeSize, wind, cirrusStrength, ShapeTex, DetailTex, WeatherTex);
            WriteColor(output, rgb, 1.0);
            return output;
        }
        WriteColor(output, CloudDebugMiss(), 1.0);
        return output;
    }

    [loop]
    for (int i = 0; i < CLOUD_MAX_STEPS; i++)
    {
        if (i >= stepLimit || transmittance < 0.02 || t > t1)
            break;
        marchSteps++;
        // Nubis Evolved distance adjustment from the *sample* along the ray.
        // Cap growth so orbit chords do not jump to 150–200 m mush steps.
        float fineStep = fineBase + 40.0 * saturate(t / 16384.0);
        fineStep = min(fineStep, fineBase * 2.5);
        float searchStep = min(max(fineStep * 4.0, 160.0), 640.0);
        if (deckFollow > 0.5)
        {
            float far = saturate(t / max(horizonT, 1.0));
            fineStep = max(fineStep, fineBase * lerp(1.0, 4.0, far));
            searchStep = min(max(fineStep * 4.0, 160.0), lerp(640.0, max(horizonT / 64.0, 640.0), far));
        }
        float3 rayPos = origin + rayDir * t;
        float3 samplePos = deckFollow > 0.5 ? CloudFlatDeckPos(rayDir, t, center) : rayPos;

        if (!fineMarch)
        {
            marchCoarse++;
            // Cheap potential: weather×height only (dimensional profile gate).
            // Matches Cubed "if (dimensional_profile > 0)" before uprez cost.
            float coarseExt;
            float potential = CloudWeatherDensity(samplePos, center, inner, outer,
                coverage, wind, 0, 0, TerrainHug, WeatherTex, coarseExt);
            if (potential < 0.005 && !CloudStormNearFunnel(samplePos))
            {
                t = AnomalyVolumeAdvance(t, t1, potential, fineStep, searchStep);
                continue;
            }

            // Low-frequency shape probe before committing fine budget.
            float4 coarseWeather;
            float coarseDensity = CloudBaseFromWeather(samplePos, center, inner, outer,
                coverage, volumeSize, searchStep, wind, cirrusStrength, 0, 0, TerrainHug,
                ShapeTex, WeatherTex, coarseWeather);
            if (coarseDensity < 0.003 && !CloudStormNearFunnel(samplePos))
            {
                t = min(t + searchStep, t1 + 1.0);
                continue;
            }

            fineMarch = true;
            emptyFine = 0;
            t = max(t0, t - searchStep);
            continue;
        }

        // Fine step: full Nubis density sample (Evolved noise body + HHF detail).
        marchFine++;
        float4 weather;
        float base = CloudBaseFromWeather(samplePos, center, inner, outer, coverage,
            volumeSize, fineStep, wind, cirrusStrength, 0, 0, TerrainHug,
            ShapeTex, WeatherTex, weather);
        float density = CloudDensityFromBase(base, samplePos, center, volumeSize, fineStep, inner,
            wind, weather, DetailTex);
        float stormPart = CloudStormDensity(samplePos, center, WeatherTex);
        stormVis = max(stormVis, stormPart);
        density = saturate(density * densityMul * CloudViewRangeFade(rayPos) + stormPart);
        if (density < 0.003)
        {
            emptyFine++;
            t = min(t + fineStep, t1 + 1.0);
            if (emptyFine >= CLOUD_FINE_EMPTY_EXIT)
            {
                fineMarch = false;
                emptyFine = 0;
                marchEmptyExits++;
            }
            continue;
        }
        emptyFine = 0;

        float stepLen = min(fineStep, max(t1 - t, 0.0));
        if (stepLen <= 1e-4)
            break;
        if (debugMode > 0)
        {
            transmittance *= CloudSegmentTransmittance(density, stepLen);
            t += stepLen;
            continue;
        }

        float sunVis = AnomalySunTransmittance(samplePos, center, CloudSunPlanetR(inner), airTop);
        float extSun = 1.0;
        float odSun = CloudSunColumnOd(samplePos, center, inner, outer, coverage,
            volumeSize, wind, cirrusStrength, TerrainHug, sunToward, sunVis,
            lightSteps, ShapeTex, WeatherTex, extSun);
        float3 sunLitCol = sunCol * lerp(1.0, hdrLift, smoothstep(0.72, 1.0, saturate(sunVis)));
        float h = CloudAltitude01(samplePos, center, inner, outer);
        float3 litAlbedo = albedo;
        float3 sky = AnomalyVolumeNight(litAlbedo, sunVis);
        float3 radiance = CloudLitRadiance(litAlbedo, sunLitCol, sky, h, density,
            dot(rayDir, sunToward), sunVis, odSun);
        float weight = CloudSegmentInscatter(density, stepLen);
        if (hitT < 0.0 && weight > 1e-4)
            hitT = t;
        accum += transmittance * radiance * weight;
        transmittance *= CloudSegmentTransmittance(density, stepLen);
        t += stepLen;
    }

    if (debugMode == CLOUD_DEBUG_STORM_BODY)
    {
        WriteColor(output, CloudDebugRamp(saturate(stormVis * 3.0)), stormVis > 0.02 ? 1.0 : 0.0);
        return output;
    }

    if (CloudDebugIsMarch(debugMode))
    {
        WriteColor(output, CloudDebugMarchColor(debugMode, marchSteps, marchCoarse, marchFine,
            marchEmptyExits, transmittance, stepLimit), 1.0);
        return output;
    }

    float alpha = (1.0 - transmittance) * fade;
    if (alpha < 0.008)
        alpha = 0.0;
    float packed = CloudPackHit(alpha, hitT > 0.0 ? hitT : 0.0);
    float3 lit = accum * fade;
    output = all(isfinite(lit)) ? float4(max(lit, 0.0), packed) : 0.0;
    return output;
}
