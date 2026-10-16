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

namespace VisualPinball.Unity.Urp
{
	// HDRP's planar and triplanar UV mapping (_UVBase 4 and 5): textures are projected from world space
	// instead of sampled with UV0. URP/Lit has no such mapping, so these profiles use a URP/Lit variant
	// (Hidden/VPE/URP/Lit Triplanar) that only replaces the texture sampling. It has the same properties and
	// keywords as URP/Lit, so the resolver sets it up exactly like a URP/Lit material.
	internal static class UrpProjectedMapping
	{
		public enum Mode
		{
			None,
			Planar,
			Triplanar,
		}

		private const string PlanarKeyword = "_VPE_MAPPING_PLANAR";

		private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

		private static Shader _shader;
		private static bool _shaderFailed;

		public static Mode Of(VpeLitProfile lit) => lit.UvBase switch {
			4 => Mode.Planar,
			5 => Mode.Triplanar,
			_ => Mode.None,
		};

		public static Shader Shader {
			get {
				if (_shader || _shaderFailed) {
					return _shader;
				}
				var shader = Resources.Load<Shader>("Shaders/VpeUrpLitTriplanar");
				if (!shader || !shader.isSupported) {
					_shaderFailed = true;
					Debug.LogWarning("URP triplanar shader unavailable; planar and triplanar materials fall back to UV0.");
					return null;
				}
				return _shader = shader;
			}
		}

		public static void Apply(Material material, Mode mode, VpeLitProfile lit)
		{
			// HDRP projects the absolute world position scaled by _TexWorldScale through the base map's tiling
			// and offset, for all of the material's maps. The shader projects the plain world position, so the
			// world scale goes into the tiling.
			var texture = lit.BaseColor?.Texture;
			var scale = texture?.Scale ?? Vector2.one;
			var offset = texture?.Offset ?? Vector2.zero;
			var worldScale = lit.Hdrp?.TexWorldScale ?? 1f;
			material.SetTextureScale(BaseMapId, scale * worldScale);
			material.SetTextureOffset(BaseMapId, offset);
			if (mode == Mode.Planar) {
				material.EnableKeyword(PlanarKeyword);
			} else {
				material.DisableKeyword(PlanarKeyword);
			}
		}
	}
}
