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
	/// anti-aliasing, global Volume bloom, the shadows of the lights around the table and the active
	/// pipeline's SSR/SSAO renderer features.
	/// </summary>
	public sealed class UrpGraphicsApplier : IVpeGraphicsApplier
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register()
		{
			RestoreUpscalerState();
			RestoreShadowState();
			Originals.Clear();
			_upscaler = VpeUpscaler.Off;
			_antiAliasing = -1;
			VpeGraphics.Register(new UrpGraphicsApplier());
			VpeGraphics.UpscalerSupport = IsUpscalerSupported;
			VpeGraphics.MotionBlurSupported = true;
			VpeGraphics.EffectSupport = IsEffectSupported;
			VpeGraphics.EffectiveSettingsSource = DescribeEffective;
			VpeGraphics.RenderScaleSource = () => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset p ? p.renderScale : 1f;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
			UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
			// scripts recompiled while playing: what the assets were set up with is forgotten with this class
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= RestoreAssets;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RestoreAssets;
#endif
		}

#if UNITY_EDITOR
		private static readonly System.Collections.Generic.Dictionary<ScriptableRendererFeature, bool> OriginalFeatureStates = new();

		private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
		{
			if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
			RestoreAssets();
		}

		// The pipeline's assets as they were before play changed them: they are shared with the project.
		private static void RestoreAssets()
		{
			foreach (var item in OriginalFeatureStates) {
				if (item.Key != null) item.Key.SetActive(item.Value);
			}
			OriginalFeatureStates.Clear();
			RestoreUpscalerState();
			RestoreShadowState();
		}
#endif

		// What URP renders the camera with: the volume stack as it resolved it, the renderer features that are
		// active and the pipeline asset's shadow setup.
		private static string DescribeEffective(Camera camera)
		{
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			var stack = VolumeManager.instance.stack;
			var bloom = stack?.GetComponent<Bloom>();
			var blur = stack?.GetComponent<MotionBlur>();
			var features = new System.Collections.Generic.List<string>();
			if (pipeline != null) {
				foreach (var renderer in pipeline.rendererDataList) {
					if (renderer == null) continue;
					foreach (var feature in renderer.rendererFeatures) {
						if (feature != null && feature.isActive) features.Add(feature.GetType().Name);
					}
				}
			}
			var shadowLights = 0;
			foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) {
				if (light.enabled && light.shadows != LightShadows.None) shadowLights++;
			}
			return string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"asset {0}; aa {1}; bloom {2:0.###}; features {3}; shadows {4} light(s), {5} cascade(s), main map {6}; motion blur {7:0.##}; scale {8:0.###}",
				pipeline != null ? pipeline.name : "none",
				camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) ? data.antialiasing.ToString() : "?",
				bloom != null && bloom.active ? bloom.intensity.value : 0f,
				features.Count > 0 ? string.Join("+", features) : "none",
				shadowLights, pipeline != null ? pipeline.shadowCascadeCount : 0, pipeline != null ? pipeline.mainLightShadowmapResolution : 0,
				blur != null && blur.active ? blur.intensity.value : 0f,
				pipeline != null ? pipeline.renderScale : 1f);
		}

		// SSR and SSAO are renderer features of the active pipeline asset's renderers, which a tier can come
		// without; URP has no screen-space global illumination.
		private static bool IsEffectSupported(VpeEffect effect) => effect switch {
			VpeEffect.ScreenSpaceReflections => HasRendererFeature("UniversalScreenSpaceReflection.ScreenSpaceReflection"),
			VpeEffect.AmbientOcclusion => HasRendererFeature("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion"),
			_ => false,
		};

		private static bool HasRendererFeature(string typeName)
		{
			if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline) {
				return false;
			}
			foreach (var renderer in pipeline.rendererDataList) {
				if (renderer == null) {
					continue;
				}
				foreach (var feature in renderer.rendererFeatures) {
					if (feature != null && feature.GetType().FullName == typeName) {
						return true;
					}
				}
			}
			return false;
		}

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
			_shadows = (VpeShadows)Mathf.Clamp(settings.Shadows, 0, 2);
			ApplyShadows();
		}

		// The lights around the table (the room's and the studio setups') are the only ones that cast shadows:
		// the lamps on the table never do. Off takes their shadows away; Low gives them hard edges, and the
		// main light one small cascade. What the lights and the pipeline asset were set up with is kept, for
		// High to give back.
		private const int LowShadowResolution = 1024;
		private static VpeShadows _shadows = VpeShadows.High;
		private static readonly System.Collections.Generic.Dictionary<Light, LightShadows> ShadowLights = new();
		private static readonly System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, (int Cascades, int Resolution)> ShadowPipelines = new();

		private static void RestoreShadowState()
		{
			foreach (var item in ShadowLights) {
				if (item.Key != null) item.Key.shadows = item.Value;
			}
			ShadowLights.Clear();
			foreach (var item in ShadowPipelines) {
				if (item.Key == null) continue;
				item.Key.shadowCascadeCount = item.Value.Cascades;
				item.Key.mainLightShadowmapResolution = item.Value.Resolution;
			}
			ShadowPipelines.Clear();
			_shadows = VpeShadows.High;
		}

		private static void ApplyShadows()
		{
			if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline) {
				if (_shadows == VpeShadows.Low) {
					if (!ShadowPipelines.ContainsKey(pipeline)) {
						ShadowPipelines[pipeline] = (pipeline.shadowCascadeCount, pipeline.mainLightShadowmapResolution);
					}
					pipeline.shadowCascadeCount = 1;
					pipeline.mainLightShadowmapResolution = Mathf.Min(LowShadowResolution, ShadowPipelines[pipeline].Resolution);
				} else if (ShadowPipelines.Remove(pipeline, out var original)) {
					pipeline.shadowCascadeCount = original.Cascades;
					pipeline.mainLightShadowmapResolution = original.Resolution;
				}
			}
			var gone = new System.Collections.Generic.List<Light>();
			foreach (var light in ShadowLights.Keys) {
				if (light == null) gone.Add(light);
			}
			foreach (var light in gone) {
				ShadowLights.Remove(light);
			}
			foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include)) {
				ApplyLightShadows(light);
			}
		}

		/// <summary>
		/// Gives a light the shadows the graphics settings allow. For lights set up after the settings were
		/// applied, such as a studio setup's.
		/// </summary>
		internal static void ApplyLightShadows(Light light)
		{
			if (light == null) {
				return;
			}
			if (!ShadowLights.TryGetValue(light, out var original)) {
				if (light.shadows == LightShadows.None || _shadows == VpeShadows.High) {
					return; // casts none, or keeps what it has
				}
				original = light.shadows;
				ShadowLights[light] = original;
			}
			switch (_shadows) {
				case VpeShadows.Off:
					light.shadows = LightShadows.None;
					break;
				case VpeShadows.Low:
					light.shadows = LightShadows.Hard;
					break;
				default:
					light.shadows = original;
					ShadowLights.Remove(light);
					break;
			}
		}

		/// <summary>
		/// A light was set up anew with these shadows: what the settings kept of its earlier ones no longer
		/// applies.
		/// </summary>
		internal static void LightConfigured(Light light)
		{
			if (light != null) {
				ShadowLights.Remove(light);
			}
			ApplyLightShadows(light);
		}

		// What a volume parameter held before a setting overrode it, with the component it belongs to: a
		// setting that's turned up again gives the volume's own value back.
		private static readonly System.Collections.Generic.Dictionary<VolumeParameter, (VolumeComponent Owner, bool Overridden, object Value)> Originals = new(new ByReference());

		// A volume parameter equals another one with the same value, and its hash changes with its value: as a
		// key it has to be the object itself.
		private sealed class ByReference : System.Collections.Generic.IEqualityComparer<VolumeParameter>
		{
			public bool Equals(VolumeParameter a, VolumeParameter b) => ReferenceEquals(a, b);
			public int GetHashCode(VolumeParameter parameter) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(parameter);
		}

		private static void Force<T>(VolumeComponent owner, VolumeParameter<T> parameter, T value)
		{
			if (!Originals.ContainsKey(parameter)) {
				Originals[parameter] = (owner, parameter.overrideState, parameter.value);
			}
			parameter.Override(value);
		}

		private static void Release<T>(VolumeParameter<T> parameter)
		{
			if (Originals.Remove(parameter, out var original)) {
				parameter.value = (T)original.Value;
				parameter.overrideState = original.Overridden;
			}
		}

		private static void ForgetOrphanedOriginals()
		{
			var orphaned = new System.Collections.Generic.List<VolumeParameter>();
			foreach (var entry in Originals) {
				if (entry.Value.Owner == null) orphaned.Add(entry.Key);
			}
			foreach (var parameter in orphaned) {
				Originals.Remove(parameter);
			}
		}

		// The SSR feature is an optional third-party assembly, so its volume component is found by name. Its
		// rays take up to 256 steps as it comes; Medium and Low cut them short, which mostly shortens long
		// reflections.
		private const string ReflectionVolumeType = "UniversalScreenSpaceReflection.ScreenSpaceReflectionVolume";

		private static void ApplyReflectionQuality(VolumeProfile profile, VpeReflectionQuality quality)
		{
			foreach (var component in profile.components) {
				if (component == null || component.GetType().FullName != ReflectionVolumeType) {
					continue;
				}
				if (component.GetType().GetField("rayMaxIterations")?.GetValue(component) is not VolumeParameter<int> iterations) {
					continue;
				}
				if (quality == VpeReflectionQuality.High) {
					Release(iterations);
				} else {
					Force(component, iterations, quality == VpeReflectionQuality.Medium ? 128 : 64);
				}
			}
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
			ForgetOrphanedOriginals();
			var profile = best != null ? best.profile : null;
			if (profile == null) {
				return;
			}
			// The pipeline asset's own volume profile is the stack's default, and an inactive component hands
			// over to it: bloom that's off needs a component that stays active and says so.
			if (!profile.TryGet<Bloom>(out var bloom) && !settings.Bloom) {
				bloom = profile.Add<Bloom>();
			}
			if (bloom != null) {
				bloom.active = true;
				if (settings.Bloom) {
					Release(bloom.intensity);
				} else {
					Force(bloom, bloom.intensity, 0f);
				}
			}
			ApplyReflectionQuality(profile, (VpeReflectionQuality)Mathf.Clamp(settings.ReflectionQuality, 0, 2));
			// The camera stands still while the balls move, so the blur has to follow the objects' own motion.
			var level = (VpeMotionBlur)settings.MotionBlur;
			if (!profile.TryGet<MotionBlur>(out var motionBlur)) {
				if (level == VpeMotionBlur.Off) {
					return;
				}
				motionBlur = profile.Add<MotionBlur>();
			}
			motionBlur.active = level != VpeMotionBlur.Off;
			motionBlur.mode.Override(MotionBlurMode.CameraAndObjects);
			motionBlur.quality.Override(MotionBlurQuality.Medium);
			// the share of the frame time the shutter stays open; High streaks a fast ball by less than half its width
			motionBlur.intensity.Override(level switch {
				VpeMotionBlur.Low => 0.15f,
				VpeMotionBlur.Medium => 0.3f,
				VpeMotionBlur.High => 0.45f,
				_ => 0f,
			});
		}
	}
}
