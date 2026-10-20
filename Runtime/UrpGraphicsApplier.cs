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
	/// anti-aliasing, global Volume bloom and the active pipeline's SSR/SSAO renderer features.
	/// </summary>
	public sealed class UrpGraphicsApplier : IVpeGraphicsApplier
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register()
		{
			RestoreUpscalerState();
			_upscaler = VpeUpscaler.Off;
			_antiAliasing = -1;
			VpeGraphics.Register(new UrpGraphicsApplier());
			VpeGraphics.UpscalerSupport = IsUpscalerSupported;
			VpeGraphics.RenderScaleSource = () => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset p ? p.renderScale : 1f;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
			UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
#endif
		}

#if UNITY_EDITOR
		private static readonly System.Collections.Generic.Dictionary<ScriptableRendererFeature, bool> OriginalFeatureStates = new();

		private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
		{
			if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
			foreach (var item in OriginalFeatureStates) {
				if (item.Key != null) item.Key.SetActive(item.Value);
			}
			OriginalFeatureStates.Clear();
			RestoreUpscalerState();
		}
#endif

		// the user's choice (-1 = none yet, the camera keeps its own)
		private static int _antiAliasing = -1;
		private static VpeUpscaler _upscaler;
		private static int _upscalerQuality;

		private struct PipelineState
		{
			public float Scale;
			public int Msaa;
			public UpscalingFilterSelection Filter;
#if ENABLE_UPSCALER_FRAMEWORK
			public string Name;
#endif
		}
		private struct CameraState { public bool PostProcessing, DynamicResolution; public VpeUpscaler LastUpscaler; public float LastScale; }
		private static readonly System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, PipelineState> PipelineStates = new();
		private static readonly System.Collections.Generic.Dictionary<Camera, CameraState> CameraStates = new();

		private static bool IsUpscalerSupported(VpeUpscaler upscaler) => upscaler switch {
			VpeUpscaler.Off => true,
			VpeUpscaler.Fsr1 => FSRUtils.IsSupported(),
			VpeUpscaler.Stp => STP.IsSupported(),
			_ => false,
		};

		// Ratios are absolute output-resolution fractions, not multiplied by the preset's render scale.
		private static float UpscalerScale(int quality) => Mathf.Clamp(quality, 0, 3) switch {
			0 => 1f / 1.5f,
			1 => 1f / 1.7f,
			2 => 0.5f,
			_ => 1f / 3f,
		};

		private static void RestoreUpscalerState()
		{
			foreach (var item in PipelineStates) {
				if (item.Key == null) continue;
				RestorePipeline(item.Key, item.Value);
			}
			PipelineStates.Clear();
			foreach (var item in CameraStates) {
				if (item.Key == null) continue;
				item.Key.allowDynamicResolution = item.Value.DynamicResolution;
				if (item.Key.TryGetComponent<UniversalAdditionalCameraData>(out var data)) data.renderPostProcessing = item.Value.PostProcessing;
			}
			CameraStates.Clear();
		}

		private static void RestorePipeline(UniversalRenderPipelineAsset pipeline, PipelineState original)
		{
			pipeline.renderScale = original.Scale;
			pipeline.msaaSampleCount = original.Msaa;
			pipeline.upscalingFilter = original.Filter;
#if ENABLE_UPSCALER_FRAMEWORK
			pipeline.upscalerName = original.Name;
#endif
		}

		private static VpeUpscaler ApplyUpscaler(Camera camera, UniversalAdditionalCameraData data)
		{
			if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline) return VpeUpscaler.Off;
			if (!PipelineStates.TryGetValue(pipeline, out var original)) {
				original = new PipelineState { Scale = pipeline.renderScale, Msaa = pipeline.msaaSampleCount, Filter = pipeline.upscalingFilter };
#if ENABLE_UPSCALER_FRAMEWORK
				original.Name = pipeline.upscalerName;
#endif
				PipelineStates.Add(pipeline, original);
			}
			if (!CameraStates.TryGetValue(camera, out var cameraOriginal)) {
				cameraOriginal = new CameraState { PostProcessing = data.renderPostProcessing, DynamicResolution = camera.allowDynamicResolution, LastScale = original.Scale };
				CameraStates.Add(camera, cameraOriginal);
			}
			var effective = IsUpscalerSupported(_upscaler) ? _upscaler : VpeUpscaler.Off;
			// STP shares URP TAA's restriction against camera stacks and overlay cameras.
			if (effective == VpeUpscaler.Stp && (data.renderType == CameraRenderType.Overlay || data.cameraStack?.Count > 0 || CabinetProjection.EffectiveAntiAliasing(camera, VpeAntiAliasing.Taa) != VpeAntiAliasing.Taa)) effective = VpeUpscaler.Off;
			if (effective == VpeUpscaler.Off) {
				RestorePipeline(pipeline, original);
				if (cameraOriginal.LastUpscaler != VpeUpscaler.Off || !Mathf.Approximately(cameraOriginal.LastScale, original.Scale)) data.resetHistory = true;
				cameraOriginal.LastUpscaler = VpeUpscaler.Off;
				cameraOriginal.LastScale = original.Scale;
				CameraStates[camera] = cameraOriginal;
				data.renderPostProcessing = cameraOriginal.PostProcessing;
				camera.allowDynamicResolution = cameraOriginal.DynamicResolution;
				return effective;
			}
			pipeline.renderScale = UpscalerScale(_upscalerQuality);
			pipeline.msaaSampleCount = effective == VpeUpscaler.Stp ? 1 : original.Msaa;
			pipeline.upscalingFilter = effective == VpeUpscaler.Stp ? UpscalingFilterSelection.STP : UpscalingFilterSelection.FSR;
#if ENABLE_UPSCALER_FRAMEWORK
			pipeline.upscalerName = effective == VpeUpscaler.Stp ? "Spatial-Temporal Post-Processing" : "FidelityFX Super Resolution 1.0";
#endif
			data.renderPostProcessing = true;
			camera.allowDynamicResolution = false;
			if (cameraOriginal.LastUpscaler != effective || !Mathf.Approximately(cameraOriginal.LastScale, pipeline.renderScale)) data.resetHistory = true;
			cameraOriginal.LastUpscaler = effective;
			cameraOriginal.LastScale = pipeline.renderScale;
			CameraStates[camera] = cameraOriginal;
			return effective;
		}

		public void Apply(VpeGraphicsSettings settings)
		{
			_antiAliasing = Mathf.Clamp(settings.AntiAliasing, 0, 3);
			_upscaler = (VpeUpscaler)settings.Upscaler;
			_upscalerQuality = Mathf.Clamp(settings.UpscalerQuality, 0, 3);
			ApplyCameraAntialiasing(Camera.main);
			ApplyVolume(settings);
			ApplyRendererFeatures(settings);
		}

		private static void ApplyRendererFeatures(VpeGraphicsSettings settings)
		{
			if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline) {
				return;
			}
			foreach (var renderer in pipeline.rendererDataList) {
				if (renderer == null) {
					continue;
				}
				foreach (var feature in renderer.rendererFeatures) {
					if (feature == null) {
						continue;
					}
					// SSR is an optional third-party assembly; match its type rather than its editable asset name.
					switch (feature.GetType().FullName) {
						case "UniversalScreenSpaceReflection.ScreenSpaceReflection":
							SetFeatureActive(feature, settings.ScreenSpaceReflections);
							break;
						case "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion":
							SetFeatureActive(feature, settings.AmbientOcclusion);
							break;
					}
				}
			}
		}

		private static void SetFeatureActive(ScriptableRendererFeature feature, bool enabled)
		{
#if UNITY_EDITOR
			if (Application.isPlaying && !OriginalFeatureStates.ContainsKey(feature)) {
				OriginalFeatureStates.Add(feature, feature.isActive);
			}
#endif
			feature.SetActive(enabled);
		}

		/// <summary>The chosen anti-aliasing, or what the camera's cabinet projection allows instead.</summary>
		internal static void ApplyCameraAntialiasing(Camera camera)
		{
			if (camera == null || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) {
				return;
			}
			var upscaler = ApplyUpscaler(camera, data);
			if (_antiAliasing >= 0) {
				var requested = upscaler == VpeUpscaler.Stp ? VpeAntiAliasing.Taa : (VpeAntiAliasing)_antiAliasing;
				var aa = CabinetProjection.EffectiveAntiAliasing(camera, requested);
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
