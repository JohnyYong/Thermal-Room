#ifndef CHAR_SAMPLE_INCLUDED
#define CHAR_SAMPLE_INCLUDED

TEXTURE3D(_CharTex);
SAMPLER(sampler_CharTex);
float3 _ThermalVolumeMin;
float3 _ThermalVolumeSize;

void SampleChar_float(float3 WorldPos, out float Char)
{
	Char = 0;

#if !defined(SHADERGRAPH_PREVIEW)
	float3 uvw = (WorldPos - _ThermalVolumeMin) / _ThermalVolumeSize;

	// 1 inside the thermal volume, 0 outside -- avoids an early return,
	// which Shader Graph can't inline safely
	float inside = (all(uvw >= 0.0) && all(uvw <= 1.0)) ? 1.0 : 0.0;

	// CharVolume already stores 0-1, no remap needed
	Char = SAMPLE_TEXTURE3D_LOD(
		_CharTex, sampler_CharTex, saturate(uvw), 0).r * inside;
#endif
}

#endif
