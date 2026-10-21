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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VisualPinball.Unity.Editor;

namespace VisualPinball.Unity.Urp
{
	// Editor-side .vpe material reconstruction. Drives the same UrpMaterialResolver the player
	// uses — so property mapping has a single home — but feeds it the freshly imported texture
	// assets and persists the resulting materials as .mat assets.
	internal sealed class UrpMaterialEditorImporter : IVpeMaterialEditorImporter
	{
		public VpeMaterialImportResult Apply(
			Transform tableRoot,
			VpeMaterialsPayload payload,
			IReadOnlyDictionary<string, Texture2D> texturesById,
			string materialAssetFolder,
			Func<string, Transform> resolveNode)
		{
			if (!tableRoot || payload?.Profiles == null) {
				return new VpeMaterialImportResult();
			}

			var resolver = new UrpMaterialResolver();

			var provider = new AssetTextureProvider(texturesById);
			var profilesByName = new Dictionary<string, VpeMaterialProfile>(StringComparer.Ordinal);
			var profilesById = new Dictionary<string, VpeMaterialProfile>(StringComparer.Ordinal);
			var boundIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (var binding in payload.Bindings ?? Array.Empty<VpeMaterialBinding>()) {
				if (!string.IsNullOrEmpty(binding?.ProfileId)) { boundIds.Add(binding.ProfileId); }
			}
			foreach (var profile in payload.Profiles) {
				if (profile != null && !string.IsNullOrWhiteSpace(profile.Name)
					&& (boundIds.Count == 0 || boundIds.Contains(profile.Id))) {
					profilesByName[VpeMaterialNameUtil.NormalizeMaterialName(profile.Name)] = profile;
				}
				if (profile != null && !string.IsNullOrEmpty(profile.Id)) { profilesById[profile.Id] = profile; }
			}
			var captured = new Dictionary<RenderTexture, Texture2D>();
			var saved = new Dictionary<Texture2D, Texture2D>();
			var profilesBySlot = new Dictionary<(Renderer, int), VpeMaterialProfile>();
			if (payload.Bindings != null) {
				foreach (var binding in payload.Bindings) {
					if (binding == null || string.IsNullOrEmpty(binding.ProfileId)
						|| !profilesById.TryGetValue(binding.ProfileId, out var profile)) { continue; }
					var node = resolveNode?.Invoke(binding.NodeId);
					if (node && node.TryGetComponent<Renderer>(out var renderer)) {
						profilesBySlot[(renderer, binding.Slot)] = profile;
					}
				}
			}

			var materialsByProfile = new Dictionary<VpeMaterialProfile, Material>();
			var materialsById = new Dictionary<string, Material>(StringComparer.Ordinal);
			var appliedSlots = 0;
			foreach (var renderer in tableRoot.GetComponentsInChildren<Renderer>(true)) {
				if (!renderer) {
					continue;
				}
				var materials = renderer.sharedMaterials;
				var modified = false;
				for (var i = 0; i < materials.Length; i++) {
					var imported = materials[i];
					if (!imported) {
						continue;
					}
					var key = VpeMaterialNameUtil.NormalizeMaterialName(imported.name);
					if (!profilesBySlot.TryGetValue((renderer, i), out var profile)
						&& !profilesByName.TryGetValue(key, out profile)) {
						continue;
					}

					if (!materialsByProfile.TryGetValue(profile, out var material)) {
						material = resolver.CreateMaterial(profile, provider, imported);
						var transient = CaptureRenderTextures(material, captured);
						if (material) {
							var assetPath = $"{materialAssetFolder}/{SanitizeFileName(profile.Name ?? "Material")}-{SanitizeFileName(profile.Id ?? "material")}.mat";
							AssetDatabase.CreateAsset(material, assetPath);
							AttachTransientTextures(material, assetPath, transient, saved);
							if (!string.IsNullOrEmpty(profile.Id)) { materialsById[profile.Id] = material; }
						}
						materialsByProfile[profile] = material;
					}
					if (!material) {
						continue;
					}

					materials[i] = material;
					modified = true;
					appliedSlots++;
				}
				if (modified) {
					renderer.sharedMaterials = materials;
				}
			}
			// The editor sidecar can reference a pre-bake or bake-preview material that is
			// no longer assigned to a renderer. Persist those profiles as assets too.
			foreach (var profile in payload.Profiles) {
				if (profile == null || materialsByProfile.ContainsKey(profile)) { continue; }
				var material = resolver.CreateMaterial(profile, provider, null);
				var transient = CaptureRenderTextures(material, captured);
				if (!material) { continue; }
				var assetPath = $"{materialAssetFolder}/{SanitizeFileName(profile.Name ?? "Material")}-{SanitizeFileName(profile.Id ?? "material")}.mat";
				AssetDatabase.CreateAsset(material, assetPath);
				AttachTransientTextures(material, assetPath, transient, saved);
				if (!string.IsNullOrEmpty(profile.Id)) { materialsById[profile.Id] = material; }
			}

			ApplyRendererStates(tableRoot, payload, resolveNode);
			AssetDatabase.SaveAssets();
			return new VpeMaterialImportResult { AppliedSlots = appliedSlots, MaterialsById = materialsById };
		}

		// The resolver bakes some textures into RenderTextures (UrpMaskRemap: HDRP mask remaps URP
		// cannot express as scalars). A .mat asset cannot reference a RenderTexture, so the slot would
		// be empty after a reload. Read each one back into a Texture2D right after the material is
		// built, before anything else can release it; the resolver's cache shares targets between
		// materials, so each one is read once.
		private static List<(int id, Texture2D tex)> CaptureRenderTextures(Material material, Dictionary<RenderTexture, Texture2D> captured)
		{
			var result = new List<(int id, Texture2D tex)>();
			if (!material) {
				return result;
			}
			foreach (var id in material.GetTexturePropertyNameIDs()) {
				if (!(material.GetTexture(id) is RenderTexture rt) || !rt) {
					continue;
				}
				if (!captured.TryGetValue(rt, out var tex)) {
					tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, rt.useMipMap, !rt.sRGB) {
						name = rt.name,
						wrapModeU = rt.wrapModeU,
						wrapModeV = rt.wrapModeV,
						filterMode = rt.filterMode,
						anisoLevel = rt.anisoLevel,
					};
					var previous = RenderTexture.active;
					RenderTexture.active = rt;
					tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
					tex.Apply(true);
					RenderTexture.active = previous;
					captured[rt] = tex;
				}
				material.SetTexture(id, tex);
				result.Add((id, tex));
			}
			return result;
		}

		// Stores every texture the material references that is not an asset yet (captured masks, or
		// any Texture2D a resolver builds in memory) inside the material asset, so it survives a reload.
		private static void AttachTransientTextures(Material material, string assetPath, List<(int id, Texture2D tex)> transient, Dictionary<Texture2D, Texture2D> saved)
		{
			// CreateAsset drops references to objects that are not assets, so each captured texture is
			// saved as a linear PNG next to the materials (compressed, shared by every material that
			// uses it) and assigned again afterwards.
			foreach (var (id, tex) in transient) {
				if (!tex) {
					continue;
				}
				if (!saved.TryGetValue(tex, out var asset)) {
					// one file per distinct bake in this import (two remaps of one mask share a name)
					var stem = $"{Path.GetDirectoryName(assetPath)}/{SanitizeFileName(tex.name)}";
					var pngPath = $"{stem}.png";
					for (var n = 2; saved.Values.Any(t => t && AssetDatabase.GetAssetPath(t) == pngPath); n++) {
						pngPath = $"{stem} {n}.png";
					}
					File.WriteAllBytes(pngPath, tex.EncodeToPNG());
					AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
					if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer) {
						importer.sRGBTexture = false;
						importer.mipmapEnabled = tex.mipmapCount > 1;
						importer.wrapModeU = tex.wrapModeU;
						importer.wrapModeV = tex.wrapModeV;
						importer.filterMode = tex.filterMode;
						importer.anisoLevel = tex.anisoLevel;
						importer.SaveAndReimport();
					}
					asset = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
					saved[tex] = asset;
				}
				if (asset) {
					material.SetTexture(id, asset);
				}
			}
			if (transient.Count > 0) {
				EditorUtility.SetDirty(material);
			}
		}

		private static void ApplyRendererStates(Transform tableRoot, VpeMaterialsPayload payload, Func<string, Transform> resolveNode)
		{
			if (payload.RendererStates == null) {
				return;
			}
			foreach (var state in payload.RendererStates) {
				if (state == null) {
					continue;
				}
				Transform target = null;
				if (!string.IsNullOrEmpty(state.NodeId) && resolveNode != null) {
					target = resolveNode(state.NodeId);
				}
				if (!target || !target.TryGetComponent<Renderer>(out var renderer)) {
					continue;
				}
				renderer.shadowCastingMode = VpeMaterialEnums.ParseShadowCastingMode(state.CastShadows);
				renderer.receiveShadows = state.ReceiveShadows;
				renderer.renderingLayerMask = state.RenderingLayerMask;
			}
		}

		private static string SanitizeFileName(string name)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var chars = name.ToCharArray();
			for (var i = 0; i < chars.Length; i++) {
				if (Array.IndexOf(invalid, chars[i]) >= 0) {
					chars[i] = '_';
				}
			}
			return new string(chars);
		}

		private sealed class AssetTextureProvider : IVpeTextureProvider
		{
			private readonly IReadOnlyDictionary<string, Texture2D> _texturesById;

			public AssetTextureProvider(IReadOnlyDictionary<string, Texture2D> texturesById)
			{
				_texturesById = texturesById ?? new Dictionary<string, Texture2D>();
			}

			public Texture2D Get(string textureId)
			{
				return !string.IsNullOrEmpty(textureId) && _texturesById.TryGetValue(textureId, out var texture)
					? texture
					: null;
			}
		}
	}

	[InitializeOnLoad]
	internal static class UrpMaterialEditorImporterRegistration
	{
		static UrpMaterialEditorImporterRegistration()
		{
			VpeMaterialEditorImport.Register(new UrpMaterialEditorImporter());
		}
	}
}
