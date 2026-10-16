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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VisualPinball.Unity.Urp
{
	// HDRP reads its mask map as metallic = lerp(min, max, R), AO = lerp(min, max, G) and
	// smoothness = lerp(min, max, A). URP/Lit has no remaps: metallic = R, smoothness = A * _Smoothness
	// and AO = lerp(1, G, _OcclusionStrength). Where URP's scalars can express the remap exactly
	// (metallic 0..1, smoothness 0..max, AO min..1) the source mask is used as is. Otherwise the remap
	// is baked into a copy of the mask with a GPU blit, once per (texture, remap) and package load.
	internal static class UrpMaskRemap
	{
		private const float Epsilon = 0.001f;

		private static readonly int RemapMinId = Shader.PropertyToID("_RemapMin");
		private static readonly int RemapMaxId = Shader.PropertyToID("_RemapMax");

		private static Material _blitMaterial;
		private static bool _blitMaterialFailed;

		// Keyed by the texture provider, which lives as long as one package import. Baked masks are
		// owned by the materials of that table afterwards, exactly like the source textures.
		private static readonly ConditionalWeakTable<IVpeTextureProvider, Dictionary<Key, RenderTexture>> Caches = new();

		public readonly struct Result
		{
			// Texture for _MetallicGlossMap and _OcclusionMap. Null when every channel is constant.
			public readonly Texture Mask;
			public readonly float Metallic;
			public readonly float Smoothness;
			public readonly float OcclusionStrength;

			public Result(Texture mask, float metallic, float smoothness, float occlusionStrength)
			{
				Mask = mask;
				Metallic = metallic;
				Smoothness = smoothness;
				OcclusionStrength = occlusionStrength;
			}
		}

		public static Result Resolve(Texture2D mask, Vector2 metallicRemap, Vector2 smoothnessRemap, Vector2 aoRemap, IVpeTextureProvider provider)
		{
			// All channels constant: no texture needed, as long as there is no AO to express.
			if (IsConstant(metallicRemap) && IsConstant(smoothnessRemap) && IsConstant(aoRemap) && Near(aoRemap.x, 1f)) {
				return new Result(null, metallicRemap.x, smoothnessRemap.x, 0f);
			}

			// What URP's own scalars already express exactly.
			var metallicIdentity = Near(metallicRemap.x, 0f) && Near(metallicRemap.y, 1f);
			var smoothnessScalable = Near(smoothnessRemap.x, 0f);
			var aoScalable = Near(aoRemap.y, 1f);
			if (metallicIdentity && smoothnessScalable && aoScalable) {
				return new Result(mask, 0f, smoothnessRemap.y, 1f - aoRemap.x);
			}

			var baked = Bake(mask, metallicRemap, smoothnessRemap, aoRemap, provider);
			if (baked) {
				// A constant, unoccluded AO channel needs no occlusion map.
				var occlusionStrength = IsConstant(aoRemap) && Near(aoRemap.x, 1f) ? 0f : 1f;
				return new Result(baked, 0f, 1f, occlusionStrength);
			}

			// Bake unavailable: closest scalar approximation.
			return new Result(mask, 0f, smoothnessRemap.y, 1f - aoRemap.x);
		}

		private static RenderTexture Bake(Texture2D source, Vector2 metallicRemap, Vector2 smoothnessRemap, Vector2 aoRemap, IVpeTextureProvider provider)
		{
			var material = BlitMaterial;
			if (!material || !source) {
				return null;
			}

			var key = new Key(source, metallicRemap, smoothnessRemap, aoRemap);
			var cache = provider != null ? Caches.GetOrCreateValue(provider) : null;
			if (cache != null && cache.TryGetValue(key, out var cached) && cached && cached.IsCreated()) {
				return cached;
			}

			var mipCount = source.mipmapCount;
			var descriptor = new RenderTextureDescriptor(source.width, source.height, GraphicsFormat.R8G8B8A8_UNorm, 0, mipCount) {
				useMipMap = mipCount > 1,
				autoGenerateMips = false,
				sRGB = false,
			};
			var target = new RenderTexture(descriptor) {
				name = $"{source.name} (remapped)",
				wrapModeU = source.wrapModeU,
				wrapModeV = source.wrapModeV,
				filterMode = source.filterMode,
				anisoLevel = source.anisoLevel,
			};
			if (!target.Create()) {
				Object.Destroy(target);
				return null;
			}

			// x = R (metallic), y = G (AO), z = B (detail mask, untouched), w = A (smoothness).
			material.SetVector(RemapMinId, new Vector4(metallicRemap.x, aoRemap.x, 0f, smoothnessRemap.x));
			material.SetVector(RemapMaxId, new Vector4(metallicRemap.y, aoRemap.y, 1f, smoothnessRemap.y));
			var previous = RenderTexture.active;
			Graphics.Blit(source, target, material, 0);
			RenderTexture.active = previous;
			// The remap is affine, so mips of the remapped mask equal the remapped source mips.
			if (descriptor.useMipMap) {
				target.GenerateMips();
			}

			if (cache != null) {
				cache[key] = target;
			}
			return target;
		}

		private static Material BlitMaterial {
			get {
				if (_blitMaterial || _blitMaterialFailed) {
					return _blitMaterial;
				}
				var shader = Resources.Load<Shader>("Shaders/VpeUrpMaskRemap");
				if (!shader || !shader.isSupported) {
					_blitMaterialFailed = true;
					Debug.LogWarning("URP mask remap shader unavailable; mask-map remaps fall back to scalar approximations.");
					return null;
				}
				_blitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				return _blitMaterial;
			}
		}

		private static bool IsConstant(Vector2 remap) => Near(remap.x, remap.y);

		private static bool Near(float a, float b) => Mathf.Abs(a - b) < Epsilon;

		private readonly struct Key : System.IEquatable<Key>
		{
			private readonly Texture _texture;
			private readonly Vector4 _min;
			private readonly Vector4 _max;

			public Key(Texture texture, Vector2 metallic, Vector2 smoothness, Vector2 ao)
			{
				_texture = texture;
				_min = new Vector4(metallic.x, ao.x, 0f, smoothness.x);
				_max = new Vector4(metallic.y, ao.y, 1f, smoothness.y);
			}

			public bool Equals(Key other) => ReferenceEquals(_texture, other._texture) && _min.Equals(other._min) && _max.Equals(other._max);

			public override bool Equals(object obj) => obj is Key other && Equals(other);

			public override int GetHashCode() => System.HashCode.Combine(RuntimeHelpers.GetHashCode(_texture), _min, _max);
		}
	}
}
