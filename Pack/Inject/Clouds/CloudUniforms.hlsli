#ifndef CLOUD_UNIFORMS_HLSLI
#define CLOUD_UNIFORMS_HLSLI

#define CenterInner AnomalyPassUniform0
#define SunOuter AnomalyPassUniform1
#define AlbedoCoverage AnomalyPassUniform2
#define WindDensity AnomalyPassUniform3
#define StepsFade AnomalyPassUniform4
// StepsFade.w = lightSteps + CloudDebugView*16 (see CloudDebug.hlsli).
#define VolumeHdr AnomalyPassUniform5
// Density controls. PlanetUp used to occupy xyz but was dead: local up is
// the normalized planet-relative sample position.
#define CloudShapeScale AnomalyPassUniform6.x
#define CloudDetailScale AnomalyPassUniform6.y
#define CloudErosionStrength AnomalyPassUniform6.z
#define CamToShell AnomalyPassUniform6.w
// Uniform7.xyz is the previous frame's wind UV. .w is CloudTypeThick0.
#define CloudPrevWind AnomalyPassUniform7.xyz
#define CloudTypeThick0 AnomalyPassUniform7.w
#define CloudTypeThick1 AnomalyPassUniform8.x
#define CloudTypeThick2 AnomalyPassUniform8.y
#define CloudTypeThick3 AnomalyPassUniform8.z
#define CloudTypeThick4 AnomalyPassUniform8.w
#define TerrainHug AnomalyPassUniform9.x
#define LayerConvective AnomalyPassUniform9.y
// Uniform9.z is unused padding.
#define LayerCirrus AnomalyPassUniform9.w
#define WeatherScale AnomalyPassUniform10.x
// Shared volumetrics owns [0, nearT); legacy IsolatedMix starts at nearT.
#define CloudLegacyNearT AnomalyPassUniform10.y
#define CloudTerrainRadius AnomalyPassUniform10.z
#define AtmosphereFit AnomalyPassUniform10.w
#define CloudTypeThick5 AnomalyPassUniform11.x
#define CloudTypeThick6 AnomalyPassUniform11.y
#define CloudTypeThick7 AnomalyPassUniform11.z
#define CloudTypeThick8 AnomalyPassUniform11.w
#define CloudTypeThick9 AnomalyPassUniform12.x
#define CloudTypeShape0 AnomalyPassUniform12.y
#define CloudTypeShape1 AnomalyPassUniform12.z
#define CloudTypeShape2 AnomalyPassUniform12.w
#define CloudSeaRadius AnomalyPassUniform13.x
#define CloudHorizonRange AnomalyPassUniform13.y
// Uniform13.zw is unused padding.
#define CloudTypeShape3 AnomalyPassUniform14.x
#define CloudTypeShape4 AnomalyPassUniform14.y
#define CloudTypeShape5 AnomalyPassUniform14.z
#define CloudTypeShape6 AnomalyPassUniform14.w
#define CloudTypeShape7 AnomalyPassUniform15.x
#define CloudTypeShape8 AnomalyPassUniform15.y
#define CloudTypeShape9 AnomalyPassUniform15.z
#define CloudSampleSpacing AnomalyPassUniform15.w

#endif
