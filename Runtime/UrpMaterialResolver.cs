// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

// ReSharper disable CheckNamespace

using UnityEngine;
using UnityEngine.Rendering;
using VisualPinball.Unity;

namespace VisualPinball.Unity.Urp
{
	// Turns portable VpeMaterialProfiles (from a .vpe package's materials.json) into live
	// URP/Lit materials in the Player, replacing the placeholder materials glTFast produces for
	// the stripped table.glb. This is the URP counterpart of HdrpMaterialResolver.
	//
	// First pass: handles the portable vpe.lit core (base color/map, metallic/smoothness,
	// HDRP-style mask map split into URP metallic-gloss + occlusion, normal map, emissive,
	// surface/blend state, double-sided). Shadergraph types (metal/rubber/dmd) and decals fall
	// back to a plain lit build from the profile's Lit data, or are left to glTFast when no Lit
	// data exists.
	public sealed class UrpMaterialResolver : IVpeMaterialResolver
	{
		private static Shader _litShader;
		private static Shader LitShader => _litShader ? _litShader : (_litShader = Shader.Find("Universal Render Pipeline/Lit"));

		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
		private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
		private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
		private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
		private static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
		private static readonly int OcclusionMapId = Shader.PropertyToID("_OcclusionMap");
		private static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
		private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
		private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
		private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
		private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
		private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
		private static readonly int BlendId = Shader.PropertyToID("_Blend");
		private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
		private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
		private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
		private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
		private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
		private static readonly int CullId = Shader.PropertyToID("_Cull");

		public bool Supports(string materialType)
		{
			return materialType switch {
				VpeMaterialTypes.Lit => true,
				VpeMaterialTypes.Metal => true,
				VpeMaterialTypes.Rubber => true,
				VpeMaterialTypes.FabricSilk => true,
				VpeMaterialTypes.Unlit => true,
				_ => false,
			};
		}

		public Material CreateMaterial(VpeMaterialProfile profile, IVpeTextureProvider textures, Material importedMaterial)
		{
			if (profile == null) {
				return null;
			}

			var lit = profile.Lit;
			if (lit == null && profile.Fabric != null) {
				lit = profile.Fabric.Lit;
			}
			if (lit == null) {
				// No portable lit data (e.g. shadergraph-only or decal); keep glTFast's material.
				return null;
			}

			return BuildLit(profile.Name, lit, textures, importedMaterial);
		}

		private static Material BuildLit(string name, VpeLitProfile lit, IVpeTextureProvider textures, Material imported)
		{
			if (lit == null || !LitShader) {
				return null;
			}

			var material = new Material(LitShader) { name = name, enableInstancing = true };

			// --- base color + map ---
			material.SetColor(BaseColorId, (Color)lit.BaseColor.Color);
			var baseTex = ResolveTexture(lit.BaseColor.Texture, textures, imported, "_BaseMap");
			if (baseTex) {
				material.SetTexture(BaseMapId, baseTex);
				ApplyScaleOffset(material, BaseMapId, lit.BaseColor.Texture);
			}

			// --- metallic / smoothness ---
			material.SetFloat(MetallicId, lit.Metallic);
			material.SetFloat(SmoothnessId, lit.Smoothness);

			// --- mask map ---
			// HDRP MaskMap packs R=metallic, G=AO, B=detail, A=smoothness. URP/Lit reads metallic
			// from R and smoothness from A of _MetallicGlossMap, and AO from G of _OcclusionMap, so
			// the very same texture feeds both slots.
			if (lit.MaskMap != null && !string.IsNullOrWhiteSpace(lit.MaskMap.TextureId)
				&& lit.MaskPacking == VpeMaskPackings.HdrpMaskMap) {
				var mask = textures?.Get(lit.MaskMap.TextureId);
				if (mask) {
					material.SetTexture(MetallicGlossMapId, mask);
					material.EnableKeyword("_METALLICSPECGLOSSMAP");
					material.SetTexture(OcclusionMapId, mask);
					material.EnableKeyword("_OCCLUSIONMAP");
					material.SetFloat(OcclusionStrengthId, lit.OcclusionStrength);
				}
			}

			// --- normal map ---
			// Source normals are plain RGB (alpha=1); URP's UnpackNormalmapRGorAG reads X from R*A,
			// so a plain RGB normal samples correctly without the AG repack HDRP needs.
			var normalTex = ResolveTexture(lit.NormalMap?.TextureId, textures, imported, "_BumpMap");
			if (normalTex) {
				material.SetTexture(BumpMapId, normalTex);
				material.SetFloat(BumpScaleId, lit.NormalMap.Strength);
				material.EnableKeyword("_NORMALMAP");
				ApplyScaleOffset(material, BumpMapId, lit.NormalMap.Offset, lit.NormalMap.Scale);
			}

			// --- emissive ---
			var emissive = (Color)lit.Emissive.Color;
			if (emissive.maxColorComponent > 0.0001f) {
				material.SetColor(EmissionColorId, emissive);
				var emissiveTex = ResolveTexture(lit.Emissive.Texture, textures, imported, "_EmissionMap");
				if (emissiveTex) {
					material.SetTexture(EmissionMapId, emissiveTex);
				}
				material.EnableKeyword("_EMISSION");
				material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
			} else {
				material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
			}

			ApplySurface(material, lit);

			material.SetFloat(CullId, lit.DoubleSided ? (float)CullMode.Off : (float)CullMode.Back);
			material.doubleSidedGI = lit.DoubleSidedGi;

			return material;
		}

		private static void ApplySurface(Material material, VpeLitProfile lit)
		{
			switch (lit.SurfaceType) {
				case VpeSurfaceTypes.Transparent:
					material.SetFloat(SurfaceId, 1f);
					material.SetFloat(AlphaClipId, 0f);
					material.DisableKeyword("_ALPHATEST_ON");
					material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
					material.SetFloat(ZWriteId, 0f);
					material.SetOverrideTag("RenderType", "Transparent");
					material.renderQueue = (int)RenderQueue.Transparent;
					ApplyBlend(material, lit.BlendMode);
					break;

				case VpeSurfaceTypes.AlphaTest:
					material.SetFloat(SurfaceId, 0f);
					material.SetFloat(AlphaClipId, 1f);
					material.SetFloat(CutoffId, lit.AlphaCutoff);
					material.EnableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
					material.SetFloat(SrcBlendId, (float)BlendMode.One);
					material.SetFloat(DstBlendId, (float)BlendMode.Zero);
					material.SetFloat(ZWriteId, 1f);
					material.SetOverrideTag("RenderType", "TransparentCutout");
					material.renderQueue = (int)RenderQueue.AlphaTest;
					break;

				default: // Opaque
					material.SetFloat(SurfaceId, 0f);
					material.SetFloat(AlphaClipId, 0f);
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
					material.SetFloat(SrcBlendId, (float)BlendMode.One);
					material.SetFloat(DstBlendId, (float)BlendMode.Zero);
					material.SetFloat(ZWriteId, 1f);
					material.SetOverrideTag("RenderType", "Opaque");
					material.renderQueue = (int)RenderQueue.Geometry;
					break;
			}
		}

		private static void ApplyBlend(Material material, string blendMode)
		{
			switch (blendMode) {
				case VpeBlendModes.Additive:
					material.SetFloat(BlendId, 2f);
					material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
					material.SetFloat(DstBlendId, (float)BlendMode.One);
					break;
				case VpeBlendModes.Premultiply:
					material.SetFloat(BlendId, 1f);
					material.SetFloat(SrcBlendId, (float)BlendMode.One);
					material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
					break;
				default: // Alpha
					material.SetFloat(BlendId, 0f);
					material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
					material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
					break;
			}
		}

		private static Texture ResolveTexture(VpeTextureRef textureRef, IVpeTextureProvider textures, Material imported, string importedProperty)
		{
			return ResolveTexture(textureRef?.TextureId, textures, imported, importedProperty);
		}

		private static Texture ResolveTexture(string textureId, IVpeTextureProvider textures, Material imported, string importedProperty)
		{
			Texture texture = null;
			if (!string.IsNullOrWhiteSpace(textureId)) {
				texture = textures?.Get(textureId);
			}
			if (!texture && imported && !string.IsNullOrEmpty(importedProperty) && imported.HasProperty(importedProperty)) {
				texture = imported.GetTexture(importedProperty);
			}
			return texture;
		}

		private static void ApplyScaleOffset(Material material, int propertyId, VpeTextureRef textureRef)
		{
			if (textureRef != null) {
				ApplyScaleOffset(material, propertyId, textureRef.Offset, textureRef.Scale);
			}
		}

		private static void ApplyScaleOffset(Material material, int propertyId, Vector2 offset, Vector2 scale)
		{
			material.SetTextureOffset(propertyId, offset);
			material.SetTextureScale(propertyId, scale);
		}
	}
}
