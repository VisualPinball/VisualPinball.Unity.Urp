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

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VisualPinball.Unity.Urp
{
	/// <summary>
	/// The shader variants <see cref="UrpMaterialResolver"/> needs in a player build.
	/// </summary>
	///
	/// <remarks>
	/// The resolver builds URP/Lit and triplanar materials at runtime and switches shader_feature keywords
	/// (surface type, mask map, normal map, ...). A build only compiles the shader_feature variants that
	/// materials or variant collections in the build use, and no material in the build uses most of these.
	/// Without them, the runtime materials fall back to stripped variants: mask-driven metals turn into white
	/// dielectrics, transparent triplanar plastics lose their look. This collection lists every keyword set
	/// the resolver can produce, and lives in Resources so every URP build includes it.
	/// </remarks>
	public static class UrpShaderVariants
	{
		public const string CollectionPath = "Packages/org.visualpinball.engine.unity.urp/Assets/Resources/Shaders/VpeUrpResolverVariants.shadervariants";

		private const string LitShaderName = "Universal Render Pipeline/Lit";
		private const string TriplanarShaderPath = "Packages/org.visualpinball.engine.unity.urp/Assets/Resources/Shaders/VpeUrpLitTriplanar.shader";

		// Surface states the resolver sets (ApplySurface, ApplyBlend, BuildDecal, BuildInsert).
		private static readonly string[][] Surfaces = {
			new string[0],
			new[] { "_ALPHATEST_ON" },
			new[] { "_SURFACE_TYPE_TRANSPARENT" },
			new[] { "_SURFACE_TYPE_TRANSPARENT", "_ALPHAPREMULTIPLY_ON" },
		};

		// Mask map states (ApplyMaskMap): none, mask, mask with occlusion.
		private static readonly string[][] Masks = {
			new string[0],
			new[] { "_METALLICSPECGLOSSMAP" },
			new[] { "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" },
		};

		private static readonly string[] Toggles = { "_NORMALMAP", "_EMISSION" };

		// The shadow caster pass only has the alpha test.
		private static readonly string[][] ShadowKeywordSets = { new string[0], new[] { "_ALPHATEST_ON" } };

		// UrpProjectedMapping: triplanar without, planar with this keyword.
		private const string PlanarKeyword = "_VPE_MAPPING_PLANAR";

		[MenuItem("VPE/Shaders/Generate URP Resolver Variants")]
		public static void Generate()
		{
			var lit = Shader.Find(LitShaderName);
			var triplanar = AssetDatabase.LoadAssetAtPath<Shader>(TriplanarShaderPath);
			if (!lit || !triplanar) {
				Debug.LogError($"UrpShaderVariants: missing shader ({LitShaderName}: {(bool)lit}, triplanar: {(bool)triplanar}).");
				return;
			}

			var collection = new ShaderVariantCollection { name = System.IO.Path.GetFileNameWithoutExtension(CollectionPath) };
			var added = Add(collection, lit, PassType.ScriptableRenderPipeline, KeywordSets(false))
				+ Add(collection, triplanar, PassType.ScriptableRenderPipeline, KeywordSets(true))
				+ Add(collection, lit, PassType.ShadowCaster, ShadowKeywordSets)
				+ Add(collection, triplanar, PassType.ShadowCaster, ShadowKeywordSets);

			var existing = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
			if (existing) {
				EditorUtility.CopySerialized(collection, existing);
				EditorUtility.SetDirty(existing);
				Object.DestroyImmediate(collection);
			} else {
				AssetDatabase.CreateAsset(collection, CollectionPath);
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"UrpShaderVariants: {added} variants written to {CollectionPath}.");
		}

		/// <summary>
		/// Every shader_feature keyword set the resolver can give a material of the given shader.
		/// </summary>
		public static IEnumerable<string[]> KeywordSets(bool triplanar)
		{
			var mappings = triplanar ? new[] { new string[0], new[] { PlanarKeyword } } : new[] { new string[0] };
			foreach (var mapping in mappings) {
				foreach (var surface in Surfaces) {
					foreach (var mask in Masks) {
						for (var toggles = 0; toggles < 1 << Toggles.Length; toggles++) {
							var keywords = new List<string>(mapping);
							keywords.AddRange(surface);
							keywords.AddRange(mask);
							keywords.AddRange(Toggles.Where((_, i) => (toggles & (1 << i)) != 0));
							yield return keywords.ToArray();
						}
					}
				}
			}
		}

		/// <summary>
		/// The keyword sets the collection at <see cref="CollectionPath"/> doesn't have, or why it can't be checked.
		/// </summary>
		public static IEnumerable<string> Missing()
		{
			var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
			var lit = Shader.Find(LitShaderName);
			var triplanar = AssetDatabase.LoadAssetAtPath<Shader>(TriplanarShaderPath);
			if (!collection || !lit || !triplanar) {
				yield return $"{CollectionPath} or its shaders are missing";
				yield break;
			}
			foreach (var (shader, isTriplanar) in new[] { (lit, false), (triplanar, true) }) {
				var sets = KeywordSets(isTriplanar).Select(k => (PassType.ScriptableRenderPipeline, k))
					.Concat(ShadowKeywordSets.Select(k => (PassType.ShadowCaster, k)));
				foreach (var (passType, keywords) in sets) {
					ShaderVariantCollection.ShaderVariant variant;
					try {
						variant = new ShaderVariantCollection.ShaderVariant(shader, passType, keywords);
					} catch (System.ArgumentException) {
						continue; // not a valid set for this shader, Generate skips it as well
					}
					if (!collection.Contains(variant)) {
						yield return $"{shader.name} [{string.Join(" ", keywords)}]";
					}
				}
			}
		}

		private static int Add(ShaderVariantCollection collection, Shader shader, PassType passType, IEnumerable<string[]> keywordSets)
		{
			var added = 0;
			foreach (var keywords in keywordSets) {
				// all URP passes but the shadow caster (and motion vectors) are of the SRP type
				try {
					if (collection.Add(new ShaderVariantCollection.ShaderVariant(shader, passType, keywords))) {
						added++;
					}
				} catch (System.ArgumentException e) {
					Debug.LogWarning($"UrpShaderVariants: {shader.name} [{string.Join(" ", keywords)}]: {e.Message}");
				}
			}
			return added;
		}
	}

	/// <summary>
	/// Fails URP player builds whose resolver variant collection is missing or out of date, since the build would
	/// render runtime-built materials with stripped variants.
	/// </summary>
	public class UrpShaderVariantsBuildGuard : UnityEditor.Build.IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

		public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
		{
			if (!UsesUrp()) {
				return;
			}
			var missing = UrpShaderVariants.Missing().ToList();
			if (missing.Count > 0) {
				throw new UnityEditor.Build.BuildFailedException(
					$"VPE: the URP resolver's shader variant collection lacks {missing.Count} variant(s), e.g. {missing[0]}. " +
					"Run VPE > Shaders > Generate URP Resolver Variants and commit the result.");
			}
		}

		// the default pipeline, or the one a quality level overrides it with
		private static bool UsesUrp()
		{
			if (GraphicsSettings.defaultRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset) {
				return true;
			}
			for (var i = 0; i < QualitySettings.names.Length; i++) {
				if (QualitySettings.GetRenderPipelineAssetAt(i) is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset) {
					return true;
				}
			}
			return false;
		}
	}
}
