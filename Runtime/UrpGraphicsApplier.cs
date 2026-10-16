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
using UnityEngine.Rendering.Universal;
using VisualPinball.Unity;

namespace VisualPinball.Unity.Urp
{
	/// <summary>
	/// Applies the host app's <see cref="VpeGraphicsSettings"/> to URP at runtime: the game camera's
	/// anti-aliasing and the global Volume's bloom. The other effects are URP renderer features, which
	/// the renderer assets configure.
	/// </summary>
	public sealed class UrpGraphicsApplier : IVpeGraphicsApplier
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register() => VpeGraphics.Register(new UrpGraphicsApplier());

		// the user's choice (-1 = none yet, the camera keeps its own)
		private static int _antiAliasing = -1;

		public void Apply(VpeGraphicsSettings settings)
		{
			_antiAliasing = Mathf.Clamp(settings.AntiAliasing, 0, 3);
			ApplyCameraAntialiasing(Camera.main);
			ApplyVolume(settings);
		}

		/// <summary>The chosen anti-aliasing, or what the camera's cabinet projection allows instead.</summary>
		internal static void ApplyCameraAntialiasing(Camera camera)
		{
			if (camera == null || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) {
				return;
			}
			if (_antiAliasing >= 0) {
				var aa = CabinetProjection.EffectiveAntiAliasing(camera, (VpeAntiAliasing)_antiAliasing);
				data.antialiasing = aa switch {
					VpeAntiAliasing.Fxaa => AntialiasingMode.FastApproximateAntialiasing,
					VpeAntiAliasing.Smaa => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
					VpeAntiAliasing.Taa => AntialiasingMode.TemporalAntiAliasing,
					_ => AntialiasingMode.None,
				};
			}
			if (data.antialiasing == AntialiasingMode.TemporalAntiAliasing) {
				ApplyTemporalAntialiasing(data);
			}
		}

		// With the whole table in view, wire ramps, rails and plastic edges are thinner than a pixel, and
		// URP's TAA defaults never settle on them: the jittered samples keep landing on and off, and its
		// history clamp rejects what it accumulated (HDRP's TAA has an anti-flicker term URP's lacks).
		// Measured on a still table at 432x768, against the defaults (High, 0.9 history, full jitter, 0.9
		// clamp), these cut the flickering pixels by about 3.5x and the strongly flickering ones by 7x,
		// with a slightly sharper result. More history (0.97) flickers even less, but risks trailing a fast ball.
		private static void ApplyTemporalAntialiasing(UniversalAdditionalCameraData data)
		{
			ref var taa = ref data.taaSettings;
			taa.quality = TemporalAAQuality.VeryHigh;
			taa.baseBlendFactor = 0.95f;
			taa.jitterScale = 0.5f;
			taa.varianceClampScale = 2f;
		}

		private static void ApplyVolume(VpeGraphicsSettings settings)
		{
			var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			Volume best = null;
			foreach (var volume in volumes) {
				if (volume.isGlobal && volume.enabled && (best == null || volume.priority > best.priority)) {
					best = volume;
				}
			}
			var profile = best != null ? best.profile : null;
			if (profile != null && profile.TryGet<Bloom>(out var bloom)) {
				bloom.active = settings.Bloom;
			}
		}
	}
}
