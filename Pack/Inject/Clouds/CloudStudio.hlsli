// helpers: CloudStudio.hlsli v9
// One cartesian cumulus. The dimensional profile is the cloud. The 128³
// Perlin-Worley tile only up-rezzes it. Weather and the planet shell are
// not sampled here.
#ifndef CLOUD_STUDIO_HLSLI
#define CLOUD_STUDIO_HLSLI

#include "CloudFunnel.hlsli"

#define StudioYaw AnomalyPassUniform0.x
#define StudioViewLift AnomalyPassUniform0.y
#define StudioViewAhead AnomalyPassUniform0.z
#define StudioDebugMode AnomalyPassUniform0.w
#define StudioHalfExt AnomalyPassUniform1.xyz
#define StudioDensityMul AnomalyPassUniform1.w
#define StudioWindScroll AnomalyPassUniform2.x
#define StudioPitch AnomalyPassUniform2.y
#define StudioRoll AnomalyPassUniform2.z
#define StudioShapePeriod AnomalyPassUniform2.w
#define StudioDetailPeriod AnomalyPassUniform3.x
#define StudioErosion AnomalyPassUniform3.y
#define StudioSampleSpacing AnomalyPassUniform3.z
#define StudioLightSteps AnomalyPassUniform3.w
#define StudioAlbedo AnomalyPassUniform4.xyz
#define StudioAmount AnomalyPassUniform4.w
#define StudioStepBudget AnomalyPassUniform5.x
#define StudioJitter AnomalyPassUniform5.y
#define StudioSearch AnomalyPassUniform5.z
#define StudioTransmittance AnomalyPassUniform5.w
#define StudioExtinction AnomalyPassUniform6.x
#define StudioPhaseG AnomalyPassUniform6.y
#define StudioPhaseGBack AnomalyPassUniform6.z
#define StudioPhaseMix AnomalyPassUniform6.w
#define StudioPowder AnomalyPassUniform7.x
#define StudioAo AnomalyPassUniform7.y
#define StudioAoPower AnomalyPassUniform7.z
#define StudioMultiScatter AnomalyPassUniform7.w
#define StudioSilver AnomalyPassUniform8.x
#define StudioSunEnergy AnomalyPassUniform8.y
#define StudioAmbientEnergy AnomalyPassUniform8.z
#define StudioSkyMode AnomalyPassUniform8.w
#define StudioBaseSoft AnomalyPassUniform9.x
#define StudioCrown AnomalyPassUniform9.y
#define StudioEnvelope AnomalyPassUniform9.z
#define StudioDetailType AnomalyPassUniform9.w
#define StudioNoiseMode AnomalyPassUniform10.x
#define StudioDensityPower AnomalyPassUniform10.y
#define StudioHeightAo AnomalyPassUniform10.z
#define StudioBeerCap AnomalyPassUniform10.w
#define StudioAnvil AnomalyPassUniform11.x
#define StudioShear AnomalyPassUniform11.y
#define StudioLid AnomalyPassUniform11.z
#define StudioBreakup AnomalyPassUniform11.w
#define StudioCells AnomalyPassUniform12.x
#define StudioAspect AnomalyPassUniform12.y
#define StudioHeading AnomalyPassUniform12.z
#define StudioFill AnomalyPassUniform12.w
#define StudioVolumeMode AnomalyPassUniform13.x
#define StudioFunnelShape AnomalyPassUniform13.y
#define StudioFunnelRadius AnomalyPassUniform13.z
#define StudioFunnelHeight AnomalyPassUniform13.w
#define StudioFunnelWall AnomalyPassUniform14.x
#define StudioFunnelSpin AnomalyPassUniform14.y
#define StudioFunnelSubX AnomalyPassUniform14.z
#define StudioFunnelSubZ AnomalyPassUniform14.w
#define StudioFunnelStage AnomalyPassUniform15.x
#define StudioFunnelSkirt AnomalyPassUniform15.y
#define StudioFunnelIntensity AnomalyPassUniform15.z
#define StudioFunnelWobble AnomalyPassUniform15.w

#define STUDIO_MAX_STEPS 96
#define STUDIO_LIGHT_MAX 6
#define STUDIO_DEBUG_PROFILE 6
#define STUDIO_DEBUG_SHAPE 8
#define STUDIO_DEBUG_BASE 9
#define STUDIO_DEBUG_ERODED 10

float StudioRemap(float x, float a, float b)
{
    return saturate((x - a) / max(b - a, 1e-4));
}

float StudioNoiseLod(float dist, float step, float period, float dim)
{
    float texel = max(period, 1.0) / max(dim, 1.0);
    float proj = max(max(AnomalyProjScale.x, AnomalyProjScale.y), 1e-4);
    float pixelAngle = max(AnomalyInvPassSize.x, AnomalyInvPassSize.y) * 2.0 / proj;
    float filter = max(max(step, 1.0), max(dist, 1.0) * max(pixelAngle, 1e-5));
    return clamp(log2(max(filter / texel, 1.0)), 0.0, 5.0);
}

// local is -1..1 in the view-aligned box. Y is the box up.
// Nubis Evolved: bottom gradient, top gradient, edge gradient, coverage.
// Anvil shelves the crown (the cumulonimbus hump in CloudHeightGradient).
// Shear and aspect streak that envelope. Cells break one puff into a deck.
float StudioProfile(float3 local)
{
    float aspect = clamp(StudioAspect, 0.15, 4.0);
    float heading = clamp(StudioHeading, 0.0, 6.2832);
    float hs = sin(heading);
    float hc = cos(heading);
    float2 xz = float2(hc * local.x - hs * local.z, hs * local.x + hc * local.z);
    xz.x /= aspect;

    float y = saturate(local.y * 0.5 + 0.5);
    float shear = clamp(StudioShear, -1.5, 1.5);
    xz.x -= shear * saturate(y - 0.35);

    float anvil = saturate(StudioAnvil);
    float crownT = smoothstep(0.12, 0.88, y);
    float shelf = anvil * smoothstep(0.55, 0.78, y) * (1.0 - smoothstep(0.94, 1.0, y));
    float halfWidth = lerp(clamp(StudioEnvelope, 0.15, 1.6), 0.22, crownT * crownT * (1.0 - anvil));
    halfWidth = max(halfWidth, shelf * 1.05);

    float radial = length(xz) / max(halfWidth, 1e-3);
    float horizontal = smoothstep(0.0, 0.20, saturate(1.0 - radial));
    float cells = clamp(StudioCells, 1.0, 8.0);
    float lobe = sin(xz.x * cells * 3.14159) * sin(xz.y * cells * 3.14159);
    lobe = saturate(0.45 + 0.55 * lobe);
    horizontal *= lerp(1.0, lobe, saturate(StudioBreakup));

    float baseSoft = clamp(StudioBaseSoft, 0.02, 0.5);
    float crown = clamp(StudioCrown, 0.2, 0.98);
    float base = smoothstep(0.0, baseSoft, y);
    float roundTop = smoothstep(1.0, crown, y);
    float lid = saturate(StudioLid);
    float topPow = lerp(2.0, 0.35, lid);
    float top = pow(max(roundTop, 1e-3), topPow);
    float fill = saturate(StudioFill);
    float body = lerp(base * top * top, saturate(base * top), fill);
    body = max(body, shelf * 0.92);
    return saturate(body * horizontal * saturate(StudioAmount));
}

static float3 StudioAxisX;
static float3 StudioAxisY;
static float3 StudioAxisZ;

float3 StudioWind()
{
    return float3(12.0, 3.5, 7.0) * StudioWindScroll;
}

// View axes, then yaw (up), pitch (right), roll (view). 0 faces the camera.
float3 StudioEulerView(float3 v)
{
    float sy, cy, sp, cp, sr, cr;
    sincos(radians(clamp(StudioYaw, -180.0, 180.0)), sy, cy);
    sincos(radians(clamp(StudioPitch, -180.0, 180.0)), sp, cp);
    sincos(radians(clamp(StudioRoll, -180.0, 180.0)), sr, cr);
    float3 rolled = float3(cr * v.x - sr * v.y, sr * v.x + cr * v.y, v.z);
    float3 pitched = float3(rolled.x, cp * rolled.y + sp * rolled.z, cp * rolled.z - sp * rolled.y);
    return float3(cy * pitched.x + sy * pitched.z, pitched.y, cy * pitched.z - sy * pitched.x);
}

void StudioOrient()
{
    StudioAxisX = CloudViewToWorld(StudioEulerView(float3(1, 0, 0)));
    StudioAxisY = CloudViewToWorld(StudioEulerView(float3(0, 1, 0)));
    StudioAxisZ = CloudViewToWorld(StudioEulerView(float3(0, 0, -1)));
}

float3 StudioToBox(float3 world)
{
    return float3(dot(world, StudioAxisX), dot(world, StudioAxisY), dot(world, StudioAxisZ));
}

float3 StudioLocal(float3 world, float3 center, float3 halfExt)
{
    return (StudioToBox(world) - StudioToBox(center)) / max(halfExt, 1.0);
}

bool StudioInside(float3 local)
{
    return local.x >= -1.0 && local.x <= 1.0
        && local.y >= -1.0 && local.y <= 1.0
        && local.z >= -1.0 && local.z <= 1.0;
}

float StudioWorleyFbm(float4 n)
{
    return saturate(n.g * 0.625 + n.b * 0.250 + n.a * 0.125);
}

// Camera-origin sample and camera-relative volume center. The difference
// is metres from the volume's world position, so the tile stays put when
// the camera moves. Wind scrolls that field; it does not follow the view.
float3 StudioNoisePos(float3 camRel, float3 center)
{
    return camRel - center;
}

// Up-rez only. Profile 0 never reaches here.
// Mode 0 keeps the cumulus Perlin-Worley remap. Detail type blends a
// wispy composite (low freq in the core, high freq at the edge) toward a
// rounder billow composite. The tile is still Perlin-Worley, not Alligator.
float StudioShapeSignal(Texture3D shapeTex, float3 noisePos, float period, float lod, float profile)
{
    float3 uv = (noisePos + StudioWind()) / max(period, 1.0);
    float4 n = shapeTex.SampleLevel(AnomalyWrapSampler, uv, lod);
    float billow = saturate(n.r);
    float worley = StudioWorleyFbm(n);
    int mode = clamp((int)(StudioNoiseMode + 0.5), 0, 3);
    if (mode == 1)
        return billow;
    if (mode == 2)
        return worley;
    if (mode == 3)
        return saturate(1.0 - worley);

    float carve = lerp(0.06, saturate(n.g), 0.28);
    float body = StudioRemap(billow, carve, 1.0);
    float edge = saturate(1.0 - profile);
    float wispy = lerp(saturate(n.g), saturate(n.a), edge);
    float billowGrad = pow(saturate(profile), 0.25);
    float billowy = lerp(saturate(n.b), billow, billowGrad);
    // The rim prefers wisps. Detail type still rounds the inner edge.
    float type = saturate(StudioDetailType) * saturate(profile);
    float composite = lerp(wispy, billowy, type);
    // Erosion pushes the edge all the way onto that composite.
    // The core stays the Perlin-Worley body.
    float wisp = saturate(clamp(StudioErosion, 0.0, 4.0) * edge);
    return lerp(body, composite, wisp);
}

float StudioDetailFloor(Texture3D detailTex, float3 noisePos, float period, float lod)
{
    float3 uv = (noisePos + StudioWind() * 1.85) / max(period, 1.0);
    float4 d = detailTex.SampleLevel(AnomalyWrapSampler, uv, lod);
    return saturate(d.r * 0.625 + d.g * 0.250 + d.b * 0.125);
}

void StudioSample(float3 world, float3 center, float3 halfExt, float step, bool erode,
    Texture3D shapeTex, Texture3D detailTex, out float profile, out float shape,
    out float baseDensity, out float density)
{
    profile = 0.0;
    shape = 0.0;
    baseDensity = 0.0;
    density = 0.0;
    float3 local = StudioLocal(world, center, halfExt);
    if (!StudioInside(local))
        return;
    profile = StudioProfile(local);
    if (profile < 1e-3)
        return;
    float dist = max(length(world), 1.0);
    float shapePeriod = max(StudioShapePeriod, 1.0);
    float lod = StudioNoiseLod(dist, step, shapePeriod, 128.0);
    float3 noisePos = StudioNoisePos(world, center);
    shape = StudioShapeSignal(shapeTex, noisePos, shapePeriod, lod, profile);
    baseDensity = saturate(shape - (1.0 - profile));
    density = baseDensity;
    if (erode && baseDensity >= 1e-4 && StudioErosion >= 1e-3)
    {
        float detailPeriod = max(StudioDetailPeriod, 1.0);
        float dLod = StudioNoiseLod(dist, step, detailPeriod, 64.0);
        float hf = StudioDetailFloor(detailTex, noisePos, detailPeriod, dLod);
        float edge = saturate(1.0 - baseDensity);
        float k = lerp(0.08, 0.55, edge) * clamp(StudioErosion, 0.0, 4.0);
        density = StudioRemap(baseDensity, hf * k, 1.0);
    }
    density *= clamp(StudioDensityMul, 0.05, 2.0);
    float power = clamp(StudioDensityPower, 0.4, 2.0);
    if (abs(power - 1.0) > 0.02)
        density = pow(saturate(density), power);
}

float StudioSunOd(float3 world, float3 center, float3 halfExt, float3 sunToward, float step,
    Texture3D shapeTex, Texture3D detailTex)
{
    int steps = clamp((int)(StudioLightSteps + 0.5), 1, STUDIO_LIGHT_MAX);
    float travel = 0.0;
    float od = 0.0;
    [loop]
    for (int i = 1; i <= STUDIO_LIGHT_MAX; i++)
    {
        if (i > steps)
            break;
        float segment = max(step, 40.0);
        travel += segment;
        float3 p = world + sunToward * travel;
        float profile, shape, baseDensity, density;
        StudioSample(p, center, halfExt, segment, false, shapeTex, detailTex, profile, shape, baseDensity, density);
        od += density * segment * clamp(StudioExtinction, 0.002, 0.08);
        if (profile < 1e-3 && i > 1)
            break;
    }
    return od;
}

float3 StudioRamp(float t)
{
    t = saturate(t);
    float3 a = float3(0.03, 0.05, 0.18);
    float3 b = float3(0.08, 0.78, 0.42);
    float3 c = float3(0.96, 0.82, 0.12);
    float3 d = float3(0.92, 0.12, 0.06);
    if (t < 0.33)
        return lerp(a, b, t / 0.33);
    if (t < 0.66)
        return lerp(b, c, (t - 0.33) / 0.33);
    return lerp(c, d, (t - 0.66) / 0.34);
}

float3 StudioSky(float3 rayDir, float3 sunCol, float3 sunToward)
{
    float elev = saturate(rayDir.y * 0.5 + 0.5);
    float white = step(0.5, StudioSkyMode);
    float3 zenith = lerp(float3(0.28, 0.48, 0.92), float3(0.92, 0.93, 0.95), white);
    float3 horizon = lerp(float3(0.68, 0.78, 0.90), float3(1.0, 1.0, 1.0), white);
    float3 sky = sunCol * lerp(horizon, zenith, elev) * 0.04;
    sky += AnomalyVolumeAmbient() * lerp(horizon, zenith, elev);
    float disc = smoothstep(0.9992, 0.99975, saturate(dot(rayDir, sunToward)));
    sky += sunCol * disc * 0.25;
    return max(sky, 0.0);
}

float StudioHg(float cosTheta, float g)
{
    float g2 = g * g;
    float d = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / max(12.566 * pow(max(d, 1e-4), 1.5), 1e-4);
}

float StudioPhase(float cosTheta)
{
    float fwd = StudioHg(cosTheta, clamp(StudioPhaseG, 0.0, 0.95));
    float back = StudioHg(cosTheta, clamp(StudioPhaseGBack, -0.6, 0.2));
    return lerp(back, fwd, saturate(StudioPhaseMix));
}

float StudioScatter(float od, float cosTheta)
{
    // Beer is whether sunlight reached this sample. It does not depend on
    // the view. Henyey-Greenstein integrates to 1, so times 4π it is ~1
    // on average and only peaks when the view lines up with the sun.
    float beer = exp(-min(max(od, 0.0), max(StudioBeerCap, 0.4)));
    const float sphere = 12.5663706;
    float phase = StudioPhase(cosTheta) * sphere;
    float primary = beer * phase;
    float ms = 0.0;
    float a = 1.0;
    float b = 1.0;
    float c = 1.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float g = clamp(StudioPhaseG, 0.0, 0.95) * c;
        ms += a * exp(-od * b) * StudioHg(cosTheta, g) * sphere;
        a *= 0.5;
        b *= 0.5;
        c *= 0.5;
    }
    float glow = lerp(primary, ms, saturate(StudioMultiScatter));
    float sun = lerp(beer, glow, saturate(StudioPhaseMix));
    // Powder darkens thick optical depth. A sun-facing sample has almost
    // no depth and must stay lit.
    float thick = saturate(1.0 - exp(-od * 2.0));
    float sugar = lerp(1.0, lerp(1.0, 0.35, thick), saturate(StudioPowder));
    return sun * sugar;
}

float StudioFunnelSheath(float horiz, float radius, float wall)
{
    float outer = CloudStormTube(horiz, radius);
    float innerR = radius * saturate(1.0 - wall);
    float hole = innerR > 1.0 ? CloudStormTube(horiz, innerR) : 0.0;
    return saturate(outer - hole);
}

float StudioDeckProfile(float3 local)
{
    // Genus profile, packed into the top of the box. The funnel hangs from its base.
    float3 deck = local;
    float band = saturate((local.y - 0.02) / 0.98);
    deck.y = band * 2.0 - 1.0;
    return StudioProfile(deck);
}

void StudioFunnelSpan(float3 center, float3 halfExt, out float y0, out float y1, out float narrow)
{
    float frac = clamp(StudioFunnelHeight, 0.2, 1.0);
    // Top stays at the deck underside. Height moves the tip downward.
    y1 = center.y + halfExt.y * 0.12;
    float floorY = center.y - halfExt.y;
    float room = max(y1 - floorY, 1.0);
    y0 = y1 - room * frac;
    narrow = 1.0;
    if (StudioFunnelStage > 1.5)
    {
        y0 += (y1 - y0) * 0.35;
        narrow = 0.15;
    }
}

float StudioFunnelRaw(float3 world, float3 center, float3 halfExt)
{
    world = StudioToBox(world);
    center = StudioToBox(center);
    float y0, y1, narrow;
    StudioFunnelSpan(center, halfExt, y0, y1, narrow);
    float above = world.y - y0;
    float span = max(y1 - y0, 1.0);
    if (above < -30.0)
        return 0.0;
    // Feather into the deck instead of ending on a flat cap.
    float cap = 1.0;
    if (above > span)
        cap = 1.0 - saturate((above - span) / max(halfExt.y * 0.45, 40.0));
    if (cap <= 0.0)
        return 0.0;
    float t = saturate(above / span);
    float baseR = max(StudioFunnelRadius, 20.0);
    float rad = CloudFunnelProfile(StudioFunnelShape, t, baseR) * narrow;
    float phase = StudioFunnelSpin;
    float s, co;
    sincos(phase, s, co);
    float wob = StudioFunnelWobble * sin(above * 0.02 + phase);
    float2 axis = center.xz + float2(co, s) * wob;
    float2 off = world.xz - axis;
    float hx = dot(off, float2(co, s));
    float hy = dot(off, float2(-s, co));
    float wall = clamp(StudioFunnelWall, 0.05, 1.0);
    float intensity = clamp(StudioFunnelIntensity, 0.0, 2.0);
    float density = intensity * StudioFunnelSheath(length(float2(hx, hy)), rad, wall);
    density = max(density, intensity * 0.65 * StudioFunnelSheath(length(float2(hx - StudioFunnelSubX, hy)), rad * 0.45, wall));
    density = max(density, intensity * 0.65 * StudioFunnelSheath(length(float2(hx, hy - StudioFunnelSubZ)), rad * 0.45, wall));
    return saturate(density * cap);
}

bool StudioFunnelNear(float3 world, float3 center, float3 halfExt, float pad)
{
    world = StudioToBox(world);
    center = StudioToBox(center);
    float y0, y1, narrow;
    StudioFunnelSpan(center, halfExt, y0, y1, narrow);
    if (world.y < y0 - pad || world.y > y1 + pad)
        return false;
    float baseR = max(StudioFunnelRadius, 20.0);
    float reach = max(baseR, length(float2(StudioFunnelSubX, StudioFunnelSubZ)) + baseR * 0.45);
    reach += pad + StudioFunnelWobble;
    return length(world.xz - center.xz) < reach;
}

float StudioFunnelDensity(float3 world, float3 center, float3 halfExt, float step,
    Texture3D shapeTex, Texture3D detailTex, bool erode)
{
    float3 local = StudioLocal(world, center, halfExt);
    float deckProfile = StudioInside(local) ? StudioDeckProfile(local) : 0.0;
    float deck = 0.0;
    if (deckProfile > 1e-3)
    {
        float dist = max(length(world), 1.0);
        float shapePeriod = max(StudioShapePeriod, 1.0);
        float lod = StudioNoiseLod(dist, step, shapePeriod, 128.0);
        float3 noisePos = StudioNoisePos(world, center);
        float shape = StudioShapeSignal(shapeTex, noisePos, shapePeriod, lod, deckProfile);
        deck = saturate(shape - (1.0 - deckProfile));
        if (erode && deck >= 1e-4 && StudioErosion >= 1e-3)
        {
            float detailPeriod = max(StudioDetailPeriod, 1.0);
            float dLod = StudioNoiseLod(dist, step, detailPeriod, 64.0);
            float hf = StudioDetailFloor(detailTex, noisePos, detailPeriod, dLod);
            float edge = saturate(1.0 - deck);
            float k = lerp(0.08, 0.55, edge) * clamp(StudioErosion, 0.0, 4.0);
            deck = StudioRemap(deck, hf * k, 1.0);
        }
        deck *= clamp(StudioDensityMul, 0.05, 2.0);
    }
    float tube = StudioFunnelRaw(world, center, halfExt);
    if (tube < 1e-4)
        return deck;
    if (!erode || StudioErosion < 1e-3)
        return max(tube, deck);
    float dist = max(length(world), 1.0);
    float detailPeriod = max(StudioDetailPeriod, 1.0);
    float dLod = StudioNoiseLod(dist, step, detailPeriod, 64.0);
    float hf = StudioDetailFloor(detailTex, StudioNoisePos(world, center), detailPeriod, dLod);
    float edge = saturate(1.0 - tube);
    float k = lerp(0.08, 0.55, edge) * clamp(StudioErosion, 0.0, 4.0);
    return max(StudioRemap(tube, hf * k, 1.0), deck);
}

float StudioFunnelSunOd(float3 world, float3 center, float3 halfExt, float3 sunToward, float step,
    Texture3D shapeTex, Texture3D detailTex)
{
    int steps = clamp((int)(StudioLightSteps + 0.5), 1, STUDIO_LIGHT_MAX);
    float travel = 0.0;
    float od = 0.0;
    [loop]
    for (int i = 1; i <= STUDIO_LIGHT_MAX; i++)
    {
        if (i > steps)
            break;
        float segment = max(step, 40.0);
        travel += segment;
        float3 p = world + sunToward * travel;
        od += StudioFunnelDensity(p, center, halfExt, segment, shapeTex, detailTex, false) * segment * clamp(StudioExtinction, 0.002, 0.08);
        if (od > max(StudioBeerCap, 0.4))
            break;
    }
    return od;
}

float4 StudioMarch(float2 pixel, float2 uv, float3 center, float3 halfExt, float maxT,
    Texture3D shapeTex, Texture3D detailTex);

float4 StudioShade(float2 pixel, float2 uv, Texture3D shapeTex, Texture3D detailTex)
{
    StudioOrient();
    float3 halfExt = max(StudioHalfExt, 1.0);
    float3 center = CloudViewToWorld(float3(0.0, StudioViewLift, StudioViewAhead));
    return StudioMarch(pixel, uv, center, halfExt, 1e9, shapeTex, detailTex);
}

// Axes must already be set. Alpha is transmittance. maxT clips the march
// (scene depth for a world box, a large value for the camera studio).
float4 StudioMarch(float2 pixel, float2 uv, float3 center, float3 halfExt, float maxT,
    Texture3D shapeTex, Texture3D detailTex)
{
    halfExt = max(halfExt, 1.0);
    float3 sunToward = AnomalySunToward;
    if (dot(sunToward, sunToward) < 1e-6)
        sunToward = float3(0.2, 0.85, 0.4);
    sunToward = normalize(sunToward);
    float3 sunCol = AnomalySunColor * max(AnomalySunDiffuse, 1.0);
    float3 albedo = max(StudioAlbedo, 0.0);

    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float2 scale = max(AnomalyProjScale, 1e-6);
    float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1.0);
    float3 rayDir = normalize(CloudViewToWorld(viewRay));
    float t0, t1;
    float3 boxDir = StudioToBox(rayDir);
    float3 boxCenter = StudioToBox(center);
    if (!CloudRayAabb(0.0, boxDir, boxCenter - halfExt, boxCenter + halfExt, t0, t1))
        return float4(0.0, 0.0, 0.0, 1.0);
    t1 = min(t1, maxT);
    if (t1 <= t0)
        return float4(0.0, 0.0, 0.0, 1.0);

    int mode = (int)(StudioDebugMode + 0.5);
    bool debugField = mode == STUDIO_DEBUG_PROFILE || mode == STUDIO_DEBUG_SHAPE
        || mode == STUDIO_DEBUG_BASE || mode == STUDIO_DEBUG_ERODED;
    int stepLimit = clamp((int)(StudioStepBudget + 0.5), 16, STUDIO_MAX_STEPS);
    float fine = clamp(StudioSampleSpacing > 1.0 ? StudioSampleSpacing : 32.0, 12.0, 120.0);
    float searchMul = clamp(StudioSearch, 1.0, 8.0);
    float search = min(max(fine * searchMul, 48.0), 220.0);
    float exitT = clamp(StudioTransmittance, 0.005, 0.2);
    float jitter = saturate(StudioJitter);
    float t = t0 + AnomalyIgnFrame(pixel) * fine * jitter;
    float3 accum = 0.0;
    float transmittance = 1.0;
    float signal = 0.0;

    [loop]
    for (int i = 0; i < STUDIO_MAX_STEPS; i++)
    {
        if (i >= stepLimit || transmittance < exitT || t > t1)
            break;
        float3 world = rayDir * t;
        float3 local = StudioLocal(world, center, halfExt);
        bool funnel = StudioVolumeMode > 0.5;
        bool inside = StudioInside(local);
        float profile = 0.0;
        if (!inside)
        {
            t += search;
            continue;
        }
        if (funnel)
        {
            float deckProfile = StudioDeckProfile(local);
            if (!StudioFunnelNear(world, center, halfExt, search) && deckProfile < 0.01)
            {
                t += search;
                continue;
            }
            profile = max(StudioFunnelRaw(world, center, halfExt), deckProfile);
        }
        else
            profile = StudioProfile(local);
        if (profile < 0.01 && !funnel)
        {
            t += search;
            continue;
        }

        if (mode == STUDIO_DEBUG_PROFILE)
        {
            signal = max(signal, profile);
            t += fine;
            continue;
        }
        float shape = profile;
        float baseDensity = profile;
        float density = profile;
        if (funnel)
            density = StudioFunnelDensity(world, center, halfExt, fine, shapeTex, detailTex, true);
        else
        {
            float sampledProfile;
            StudioSample(world, center, halfExt, fine, true, shapeTex, detailTex,
                sampledProfile, shape, baseDensity, density);
        }
        if (debugField)
        {
            float s = shape;
            if (mode == STUDIO_DEBUG_BASE)
                s = baseDensity;
            else if (mode == STUDIO_DEBUG_ERODED)
                s = saturate(density);
            signal = max(signal, s);
            t += fine;
            continue;
        }
        if (density < 0.003)
        {
            t += fine;
            continue;
        }

        float stepLen = min(fine, max(t1 - t, 0.0));
        if (stepLen <= 1e-3)
            break;
        float ext = clamp(StudioExtinction, 0.002, 0.08);
        float od = funnel
            ? StudioFunnelSunOd(world, center, halfExt, sunToward, fine, shapeTex, detailTex)
            : StudioSunOd(world, center, halfExt, sunToward, fine, shapeTex, detailTex);
        float mu = dot(rayDir, sunToward);
        float scatter = StudioScatter(od, mu);
        float y = saturate(local.y * 0.5 + 0.5);
        float skin = pow(saturate(1.0 - profile), clamp(StudioAoPower, 0.2, 2.0));
        float heightShade = lerp(1.0, lerp(0.35, 1.0, y), saturate(StudioHeightAo));
        float ao = lerp(1.0, skin * heightShade, saturate(StudioAo));
        float3 sunLit = sunCol * albedo * scatter * max(StudioSunEnergy, 0.0);
        float silver = pow(saturate(mu), 8.0) * (1.0 - exp(-min(od, 2.0))) * saturate(StudioSilver);
        sunLit += sunCol * albedo * silver;
        float3 ambient = AnomalyVolumeAmbient() * albedo * ao * max(StudioAmbientEnergy, 0.0);
        float weight = 1.0 - exp(-density * stepLen * ext);
        accum += transmittance * (sunLit + ambient) * weight;
        transmittance *= exp(-density * min(stepLen, 220.0) * ext);
        t += stepLen;
    }

    if (debugField)
        return float4(signal > 0.002 ? StudioRamp(signal) : float3(0.04, 0.04, 0.05), 1.0);

    // Sky is composited after the temporal blend. Alpha is transmittance.
    if (!all(isfinite(accum)) || !isfinite(transmittance))
        return float4(0.0, 0.0, 0.0, 1.0);
    return float4(max(accum, 0.0), saturate(transmittance));
}

float4 StudioPresent(float2 uv, float4 cloud)
{
    StudioOrient();
    int mode = (int)(StudioDebugMode + 0.5);
    bool debugField = mode == STUDIO_DEBUG_PROFILE || mode == STUDIO_DEBUG_SHAPE
        || mode == STUDIO_DEBUG_BASE || mode == STUDIO_DEBUG_ERODED;
    if (mode > 0)
        return float4(max(cloud.rgb, 0.0), debugField ? 1.0 : saturate(cloud.a));
    float3 sunToward = AnomalySunToward;
    if (dot(sunToward, sunToward) < 1e-6)
        sunToward = float3(0.2, 0.85, 0.4);
    sunToward = normalize(sunToward);
    float3 sunCol = AnomalySunColor * max(AnomalySunDiffuse, 1.0);
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float2 scale = max(AnomalyProjScale, 1e-6);
    float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1.0);
    float3 rayDir = normalize(CloudViewToWorld(viewRay));
    float3 sky = StudioSky(rayDir, sunCol, sunToward);
    float3 color = cloud.rgb + sky * saturate(cloud.a);
    if (!all(isfinite(color)))
        color = sky;
    return float4(max(color, 0.0), 1.0);
}

#endif
