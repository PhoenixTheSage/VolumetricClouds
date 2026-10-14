#ifndef CLOUD_TEMPORAL_HLSLI
#define CLOUD_TEMPORAL_HLSLI

// Hit metres in the high half, opacity in the low half. 200 km covers a shell chord.
float CloudPackHit(float opacity, float hitMeters)
{
    float h = saturate(hitMeters / 200000.0);
    uint bits = f32tof16(saturate(opacity)) | (f32tof16(h) << 16);
    return asfloat(bits);
}

void CloudUnpackHit(float packed, out float opacity, out float hitMeters)
{
    uint bits = asuint(packed);
    opacity = saturate(f16tof32(bits & 0xffffu));
    hitMeters = f16tof32(bits >> 16) * 200000.0;
}

// 3x3 clip of history against the raw neighborhood, then a 1/8 new sample.
// history.a < 0 is a cleared target. accept is false for an out-of-range UV.
float4 CloudTemporalBlend(Texture2D rawTex, Texture2D histTex, SamplerState pointSamp,
    SamplerState linearSamp, float2 uv, float2 prevUv, bool accept)
{
    float4 cur = rawTex.SampleLevel(pointSamp, uv, 0);
    if (!accept || AnomalyLightingHistoryValid == 0)
        return cur;
    float2 invSize = max(AnomalyInvPassSize, 1e-6);
    float3 lo = cur.rgb;
    float3 hi = cur.rgb;
    float alo = cur.a;
    float ahi = cur.a;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float4 n = rawTex.SampleLevel(pointSamp, uv + float2(x, y) * invSize, 0);
            lo = min(lo, n.rgb);
            hi = max(hi, n.rgb);
            alo = min(alo, n.a);
            ahi = max(ahi, n.a);
        }
    }
    float4 hist = histTex.SampleLevel(linearSamp, prevUv, 0);
    if (hist.a < 0.0)
        return cur;
    hist.rgb = clamp(hist.rgb, lo, hi);
    hist.a = clamp(hist.a, alo, ahi);
    return lerp(hist, cur, 0.125);
}

#endif
