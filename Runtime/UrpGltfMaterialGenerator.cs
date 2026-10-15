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

using GLTFast.Materials;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VisualPinball.Unity;

namespace VisualPinball.Unity.Urp
{
	/// <summary>
	/// Creates the materials of a table's glTF import as stock URP/Lit materials.
	/// </summary>
	///
	/// <remarks>
	/// glTFast's default URP generator uses its own shader graphs (glTF-pbrMetallicRoughness), which
	/// aren't in player builds. Without them it can't create the imported materials, so
	/// <see cref="UrpMaterialResolver"/>, which replaces them by name, finds nothing to replace and
	/// the table renders pink. The imported materials are only transient placeholders, so like
	/// HDRP's generator, this one makes them valid, correctly named URP/Lit materials.
	/// </remarks>
	public sealed class UrpGltfMaterialGenerator : UniversalRPMaterialGenerator
	{
		private static Shader _urpLit;

		private static Shader UrpLit
		{
			get {
				if (!_urpLit) {
					_urpLit = Shader.Find("Universal Render Pipeline/Lit");
				}
				if (!_urpLit) {
					// Shader.Find can return null on very early calls; the table template is URP/Lit
					// and always in the build
					var template = Resources.Load<Material>("Materials/TableOpaque");
					if (template) {
						_urpLit = template.shader;
					}
				}
				return _urpLit;
			}
		}

		private UrpGltfMaterialGenerator(UniversalRenderPipelineAsset asset) : base(asset)
		{
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Register()
		{
			// null without an active URP asset, then glTFast's defaults apply
			RuntimeGltfMaterialGenerator.Factory = () => UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset is { } asset
				? new UrpGltfMaterialGenerator(asset)
				: null;
		}

		protected override Shader GetMetallicShader(MetallicShaderFeatures features)
		{
			return UrpLit ? UrpLit : base.GetMetallicShader(features);
		}

		protected override Material GenerateDefaultMaterial(bool pointsSupport = false)
		{
			return UrpLit
				? new Material(UrpLit) { name = DefaultMaterialName }
				: base.GenerateDefaultMaterial(pointsSupport);
		}
	}
}
