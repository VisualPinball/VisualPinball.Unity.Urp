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
using Mesh = VisualPinball.Engine.VPT.Mesh;

namespace VisualPinball.Unity.Urp
{
	public class MaterialConverter : IMaterialConverter
	{
		#region Shader Properties

		private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
		private static readonly int Metallic = Shader.PropertyToID("_Metallic");
		private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
		private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
		private static readonly int NormalMap = Shader.PropertyToID("_BumpMap");
		private static readonly int UVChannelVertices = Shader.PropertyToID("_UVChannelVertices");
		private static readonly int UVChannelNormals = Shader.PropertyToID("_UVChannelNormals");

		#endregion

		public Shader GetShader()
		{
			return Shader.Find("Universal Render Pipeline/Lit");
		}

		private Shader GetShader(PbrMaterial vpxMaterial)
		{
			return vpxMaterial.VertexLerpWithUvEnabled
				? Shader.Find("Visual Pinball/Srp/LerpVertex")
				: GetShader();
		}

		public static UnityEngine.Material GetDefaultMaterial(BlendMode blendMode)
		{
			switch (blendMode)
			{
				case Engine.VPT.BlendMode.Opaque:
					return UnityEngine.Resources.Load<UnityEngine.Material>("Materials/TableOpaque");
				case Engine.VPT.BlendMode.Cutout:
					return UnityEngine.Resources.Load<UnityEngine.Material>("Materials/TableCutout");
				case Engine.VPT.BlendMode.Translucent:
					return UnityEngine.Resources.Load<UnityEngine.Material>("Materials/TableTranslucent");
				default:
					throw new ArgumentOutOfRangeException("Undefined blend mode " + blendMode);
			}

		}

		public UnityEngine.Material CreateMaterial(PbrMaterial vpxMaterial, TableAuthoring table, Type objectType, StringBuilder debug = null)
		{
			UnityEngine.Material defaultMaterial = GetDefaultMaterial(vpxMaterial.MapBlendMode);

			var unityMaterial = new UnityEngine.Material(GetShader(vpxMaterial));
			unityMaterial.CopyPropertiesFromMaterial(defaultMaterial);
			unityMaterial.name = vpxMaterial.Id;

			if (vpxMaterial.VertexLerpWithUvEnabled) {
				unityMaterial.SetFloat(UVChannelVertices, Mesh.AnimationUVChannelVertices);
				unityMaterial.SetFloat(UVChannelNormals, Mesh.AnimationUVChannelNormals);
			}

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


			if (vpxMaterial.MapBlendMode == Engine.VPT.BlendMode.Translucent)
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
			unityMaterial.SetFloat(Smoothness, vpxMaterial.Roughness);

			// map
			if (table != null && vpxMaterial.HasMap)
			{
				unityMaterial.SetTexture(BaseMap, table.GetTexture(vpxMaterial.Map.Name));
			}

			// normal map
			if (table != null && vpxMaterial.HasNormalMap)
			{
				unityMaterial.EnableKeyword("_NORMALMAP");
				unityMaterial.EnableKeyword("_NORMALMAP_TANGENT_SPACE");

				unityMaterial.SetTexture(NormalMap, table.GetTexture(vpxMaterial.NormalMap.Name));
			}

			return unityMaterial;
		}
	}
}
