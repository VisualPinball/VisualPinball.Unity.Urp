// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
// Licensed under the GNU General Public License v3 or later.

// URP/Lit with HDRP's planar and triplanar texture mapping (HDRP Lit _UVBase 4 and 5).
//
// URP's own LitInput.hlsl is included with its InitializeStandardLitSurfaceData renamed, and the version
// below takes its place in URP's unmodified forward and G-buffer passes. URP calls it with UV0 only, so the
// pass wrappers hand the world position and vertex frame over in static variables first. Everything else,
// including the UnityPerMaterial layout, is URP/Lit's.
//
// The mapping follows HDRP: the absolute world position is projected onto the ZY, XZ and XY planes and run
// through the base map's tiling and offset (which carries HDRP's _TexWorldScale, see UrpProjectedMapping);
// the three samples are blended with ComputeTriplanarWeights of the vertex normal, and normal maps are
// combined with the same surface gradient math. Planar mapping is the XZ projection alone.

#ifndef VPE_URP_LIT_TRIPLANAR_INCLUDED
#define VPE_URP_LIT_TRIPLANAR_INCLUDED

#define InitializeStandardLitSurfaceData UrpInitializeStandardLitSurfaceData
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#undef InitializeStandardLitSurfaceData
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/NormalSurfaceGradient.hlsl"

static float3 vpeTriplanarPositionWS;
static float3 vpeTriplanarNormalWS;
// The tangent frame exactly as URP's InitializeInputData builds it (interpolated, not normalized), so the
// tangent-space normal handed back to it turns into the intended world-space normal.
static float3x3 vpeTriplanarTangentToWorld;

void VpeTriplanarSetup(float3 positionWS, float3 normalWS, float4 tangentWS)
{
	vpeTriplanarPositionWS = positionWS;
	vpeTriplanarNormalWS = normalize(normalWS);
	float3 bitangentWS = tangentWS.w * cross(normalWS, tangentWS.xyz);
	vpeTriplanarTangentToWorld = float3x3(tangentWS.xyz, bitangentWS, normalWS);
}

struct VpeTriplanarUv
{
	float2 zy;
	float2 xz;
	float2 xy;
	float3 weights;
};

VpeTriplanarUv VpeTriplanarCoordinates()
{
	VpeTriplanarUv t;
	float3 position = GetAbsolutePositionWS(vpeTriplanarPositionWS);
	t.zy = position.zy * _BaseMap_ST.xy + _BaseMap_ST.zw;
	t.xz = position.xz * _BaseMap_ST.xy + _BaseMap_ST.zw;
	t.xy = position.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;
#if defined(_VPE_MAPPING_PLANAR)
	t.weights = float3(0.0, 1.0, 0.0);
#else
	t.weights = ComputeTriplanarWeights(vpeTriplanarNormalWS);
#endif
	return t;
}

// Blends without branching on the weights: samples inside divergent branches lose their derivatives.
#define VPE_TRIPLANAR_BLEND(t, sampleFn) \
	((t).weights.x * sampleFn((t).zy) + (t).weights.y * sampleFn((t).xz) + (t).weights.z * sampleFn((t).xy))

#define VPE_SAMPLE_BASE(uv) SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap))
#define VPE_SAMPLE_OCCLUSION(uv) SampleOcclusion(uv)
#define VPE_SAMPLE_EMISSION(uv) SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap))

half4 VpeTriplanarAlbedoAlpha(VpeTriplanarUv t)
{
	return VPE_TRIPLANAR_BLEND(t, VPE_SAMPLE_BASE);
}

// World-space normal from the projected tangent-space normal maps (HDRP: SampleUVMappingNormal).
float3 VpeTriplanarNormalWS(VpeTriplanarUv t)
{
#if defined(_NORMALMAP)
	half3 nZY = SampleNormal(t.zy, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
	half3 nXZ = SampleNormal(t.xz, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
	half3 nXY = SampleNormal(t.xy, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
	float2 derivX = ConvertTangentSpaceNormalToHeightMapGradient(nZY.xy, rcp(max(nZY.z, REAL_EPS)), 1.0);
	float2 derivY = ConvertTangentSpaceNormalToHeightMapGradient(nXZ.xy, rcp(max(nXZ.z, REAL_EPS)), 1.0);
	float2 derivZ = ConvertTangentSpaceNormalToHeightMapGradient(nXY.xy, rcp(max(nXY.z, REAL_EPS)), 1.0);
	float3 surfaceGradient = SurfaceGradientFromTriplanarProjection(vpeTriplanarNormalWS, t.weights, derivX, derivY, derivZ);
	return SurfaceGradientResolveNormal(vpeTriplanarNormalWS, surfaceGradient);
#else
	return vpeTriplanarNormalWS;
#endif
}

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
	VpeTriplanarUv t = VpeTriplanarCoordinates();

	half4 albedoAlpha = VpeTriplanarAlbedoAlpha(t);
	outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

	half4 specGloss = t.weights.x * SampleMetallicSpecGloss(t.zy, albedoAlpha.a)
		+ t.weights.y * SampleMetallicSpecGloss(t.xz, albedoAlpha.a)
		+ t.weights.z * SampleMetallicSpecGloss(t.xy, albedoAlpha.a);
	outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
	outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

#if _SPECULAR_SETUP
	outSurfaceData.metallic = half(1.0);
	outSurfaceData.specular = specGloss.rgb;
#else
	outSurfaceData.metallic = specGloss.r;
	outSurfaceData.specular = half3(0.0, 0.0, 0.0);
#endif
	outSurfaceData.smoothness = specGloss.a;

	// Back into the vertex tangent frame, which is what URP expects from a surface. The frame isn't
	// orthonormal, so this needs the real inverse of URP's TransformTangentToWorld.
#if defined(_NORMALMAP)
	outSurfaceData.normalTS = half3(TransformWorldToTangent(VpeTriplanarNormalWS(t), vpeTriplanarTangentToWorld));
#else
	outSurfaceData.normalTS = half3(0.0, 0.0, 1.0);
#endif

	outSurfaceData.occlusion = VPE_TRIPLANAR_BLEND(t, VPE_SAMPLE_OCCLUSION);
	outSurfaceData.emission = VPE_TRIPLANAR_BLEND(t, VPE_SAMPLE_EMISSION);

	// Not used by VPE materials.
	outSurfaceData.clearCoatMask = half(0.0);
	outSurfaceData.clearCoatSmoothness = half(0.0);
}

#endif // VPE_URP_LIT_TRIPLANAR_INCLUDED
