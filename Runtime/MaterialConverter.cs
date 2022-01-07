// Visual Pinball Engine
// Copyright (C) 2020 freezy and VPE Team
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

// ReSharper disable StringLiteralTypo
// ReSharper disable UnusedType.Global
// ReSharper disable CheckNamespace

using System;
using System.Text;
using UnityEngine;
using VisualPinball.Engine.VPT;
using Material = UnityEngine.Material;

namespace VisualPinball.Unity.Urp
{
	public class MaterialConverter : IMaterialConverter
	{
		public Material DotMatrixDisplay => Resources.Load<Material>("Materials/Dot Matrix Display (SRP)");
		public Material SegmentDisplay => Resources.Load<Material>("Materials/Segment Display (SRP)");

		public int NormalMapProperty => NormalMap;

		#region Shader Properties

		private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
		private static readonly int Metallic = Shader.PropertyToID("_Metallic");
		private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
		private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
		private static readonly int NormalMap = Shader.PropertyToID("_BumpMap");

		#endregion

		public Shader GetShader()
		{
			return Shader.Find("Universal Render Pipeline/Lit");
		}

		private Shader GetShader(PbrMaterial vpxMaterial)
		{
			return GetShader();
		}

		public static Material GetDefaultMaterial(BlendMode blendMode)
		{
			switch (blendMode)
			{
				case BlendMode.Opaque:
					return Resources.Load<Material>("Materials/TableOpaque");
				case BlendMode.Cutout:
					return Resources.Load<Material>("Materials/TableCutout");
				case BlendMode.Translucent:
					return Resources.Load<Material>("Materials/TableTranslucent");
				default:
					throw new ArgumentOutOfRangeException("Undefined blend mode " + blendMode);
			}
		}

		public Material CreateMaterial(PbrMaterial vpxMaterial, ITextureProvider textureProvider, StringBuilder debug = null)
		{
			Material defaultMaterial = GetDefaultMaterial(vpxMaterial.MapBlendMode);

			var unityMaterial = new Material(GetShader(vpxMaterial));
			unityMaterial.CopyPropertiesFromMaterial(defaultMaterial);
			unityMaterial.name = vpxMaterial.Id;

			// apply some basic manipulations to the color. this just makes very
			// very white colors be clipped to 0.8204 aka 204/255 is 0.8
			// this is to give room to lighting values. so there is more modulation
			// of brighter colors when being lit without blow outs too soon.
			var col = vpxMaterial.Color.ToUnityColor();
			if (vpxMaterial.Color.IsGray() && col.grayscale > 0.8)
			{
				debug?.AppendLine("Color manipulation performed, brightness reduced.");
				col.r = col.g = col.b = 0.8f;
			}


			if (vpxMaterial.MapBlendMode == BlendMode.Translucent)
			{
				col.a = Mathf.Min(1, Mathf.Max(0, vpxMaterial.Opacity));
			}
			unityMaterial.SetColor(BaseColor, col);

			// validate IsMetal. if true, set the metallic value.
			// found VPX authors setting metallic as well as translucent at the
			// same time, which does not render correctly in unity so we have
			// to check if this value is true and also if opacity <= 1.
			float metallicValue = 0f;
			if (vpxMaterial.IsMetal && (!vpxMaterial.IsOpacityActive || vpxMaterial.Opacity >= 1))
			{
				metallicValue = 1f;
				debug?.AppendLine("Metallic set to 1.");
			}

			unityMaterial.SetFloat(Metallic, metallicValue);

			// roughness / glossiness
			SetSmoothness(unityMaterial, vpxMaterial.Roughness);

			// map
			if (vpxMaterial.HasMap) {
				unityMaterial.SetTexture(BaseMap, textureProvider.GetTexture(vpxMaterial.Map.Name));
			}

			// normal map
			if (vpxMaterial.HasNormalMap) {
				unityMaterial.EnableKeyword("_NORMALMAP");
				unityMaterial.EnableKeyword("_NORMALMAP_TANGENT_SPACE");

				unityMaterial.SetTexture(NormalMap, textureProvider.GetTexture(vpxMaterial.NormalMap.Name));
			}

			return unityMaterial;
		}

		public void SetSmoothness(Material material, float smoothness)
		{
			material.SetFloat(Smoothness, smoothness);
		}

		public Material MergeMaterials(PbrMaterial vpxMaterial, Material texturedMaterial)
		{
			var nonTexturedMaterial = CreateMaterial(vpxMaterial, null);
			var mergedMaterial = new Material(GetShader());
			mergedMaterial.CopyPropertiesFromMaterial(texturedMaterial);

			mergedMaterial.name = nonTexturedMaterial.name;
			mergedMaterial.SetColor(BaseColor, nonTexturedMaterial.GetColor(BaseColor));
			mergedMaterial.SetFloat(Metallic, nonTexturedMaterial.GetFloat(Metallic));
			mergedMaterial.SetFloat(Smoothness, nonTexturedMaterial.GetFloat(Smoothness));

			return mergedMaterial;
		}

		public void SetDiffusionProfile(Material material, DiffusionProfileTemplate template)
		{
			// todo
		}

		public void SetMaterialType(Material material, MaterialType materialType)
		{
			// todo
		}

		public void SetEmissiveColor(MaterialPropertyBlock propBlock, Color color)
		{
			// urp has no emissive color
		}

		public Color? GetEmissiveColor(Material material)
		{
			// urp has no emissive color

			return null;
		}
	}
}
