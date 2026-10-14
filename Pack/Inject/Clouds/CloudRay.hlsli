#ifndef CLOUD_RAY_HLSLI
#define CLOUD_RAY_HLSLI

float3 CloudViewToWorld(float3 view)
{
    return AnomalyCameraToWorld[0].xyz * view.x
         + AnomalyCameraToWorld[1].xyz * view.y
         + AnomalyCameraToWorld[2].xyz * view.z;
}

float2 CloudRaySphere(float3 origin, float3 dir, float3 center, float radius)
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

bool CloudRayAabb(float3 origin, float3 dir, float3 bmin, float3 bmax, out float t0, out float t1)
{
    float3 inv = 1.0 / max(abs(dir), 1e-8) * sign(dir + 1e-12);
    float3 tA = (bmin - origin) * inv;
    float3 tB = (bmax - origin) * inv;
    float3 tmin3 = min(tA, tB);
    float3 tmax3 = max(tA, tB);
    t0 = max(max(tmin3.x, tmin3.y), tmin3.z);
    t1 = min(min(tmax3.x, tmax3.y), tmax3.z);
    t0 = max(t0, 0.0);
    return t1 > t0;
}

#endif
