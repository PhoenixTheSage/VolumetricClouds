// Shared-medium adapter: weather + shape only. Planet SVO stays IsolatedMix.
Texture3D MediumTexture0 : register(MediumRegister0);
Texture3D MediumTexture1 : register(MediumRegister1);
Texture3D MediumTexture2 : register(MediumRegister2);
#define AnomalyPassUniform0 MediumUniforms[0]
#define AnomalyPassUniform1 MediumUniforms[1]
#define AnomalyPassUniform2 MediumUniforms[2]
#define AnomalyPassUniform3 MediumUniforms[3]
#define AnomalyPassUniform4 MediumUniforms[4]
#define AnomalyPassUniform5 MediumUniforms[5]
#define AnomalyPassUniform6 MediumUniforms[6]
#define AnomalyPassUniform7 MediumUniforms[7]
#define AnomalyPassUniform8 MediumUniforms[8]
#define AnomalyPassUniform9 MediumUniforms[9]
#define AnomalyPassUniform10 MediumUniforms[10]
#define AnomalyPassUniform11 MediumUniforms[11]
#define AnomalyPassUniform12 MediumUniforms[12]
#define AnomalyPassUniform13 MediumUniforms[13]
#define AnomalyPassUniform14 MediumUniforms[14]
#define AnomalyPassUniform15 MediumUniforms[15]
#include "../Inject/Clouds/CloudUniforms.hlsli"
#include "../Inject/Clouds/CloudDensity.hlsli"
AnomalyMediumSample EvaluateMedium(float3 p)
{
    AnomalyMediumSample m = AnomalyEmptyMedium();
    float inner = CenterInner.w, outer = SunOuter.w;
    float4 weather;
    float density = CloudBaseFromWeather(p, 0, inner, outer, AlbedoCoverage.w, VolumeHdr.x,
        max(CloudSampleSpacing, 40.0), WindDensity.xyz, StepsFade.z, 0, 0, TerrainHug,
        MediumTexture0, MediumTexture2, weather);
    density = CloudDensityFromBase(density, p, 0, VolumeHdr.x,
        max(CloudSampleSpacing, 40.0), inner, WindDensity.xyz, weather, MediumTexture1);
    density *= WindDensity.w;
    m.extinction = max(density, 0) * .01 * saturate(StepsFade.y);
    m.scattering = m.extinction * saturate(AlbedoCoverage.rgb);
    m.anisotropy = .65;
    m.velocity = MediumVelocity;
    return m;
}
#undef CenterInner
#undef SunOuter
#undef AlbedoCoverage
#undef WindDensity
#undef StepsFade
#undef VolumeHdr
#undef CloudShapeScale
#undef CloudDetailScale
#undef CloudErosionStrength
#undef CamToShell
#undef CloudTypeThick0
#undef CloudTypeThick1
#undef CloudTypeThick2
#undef CloudTypeThick3
#undef CloudTypeThick4
#undef TerrainHug
#undef LayerConvective
#undef LayerCirrus
#undef WeatherScale
#undef CloudLegacyNearT
#undef CloudTerrainRadius
#undef AtmosphereFit
#undef CloudTypeThick5
#undef CloudTypeThick6
#undef CloudTypeThick7
#undef CloudTypeThick8
#undef CloudTypeThick9
#undef CloudTypeShape0
#undef CloudTypeShape1
#undef CloudTypeShape2
#undef CloudSeaRadius
#undef CloudHorizonRange
#undef CloudTypeShape3
#undef CloudTypeShape4
#undef CloudTypeShape5
#undef CloudTypeShape6
#undef CloudTypeShape7
#undef CloudTypeShape8
#undef CloudTypeShape9
#undef CloudSampleSpacing
#undef AnomalyPassUniform0
#undef AnomalyPassUniform1
#undef AnomalyPassUniform2
#undef AnomalyPassUniform3
#undef AnomalyPassUniform4
#undef AnomalyPassUniform5
#undef AnomalyPassUniform6
#undef AnomalyPassUniform7
#undef AnomalyPassUniform8
#undef AnomalyPassUniform9
#undef AnomalyPassUniform10
#undef AnomalyPassUniform11
#undef AnomalyPassUniform12
#undef AnomalyPassUniform13
#undef AnomalyPassUniform14
#undef AnomalyPassUniform15
