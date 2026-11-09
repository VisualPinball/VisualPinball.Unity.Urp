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
	/// Lights the table's surroundings for the host app under URP: scales the skybox with the ambient light
	/// and the reflections it gives, and sets up room lights from physical units.
	/// </summary>
	public sealed class UrpEnvironmentApplier : IVpeEnvironmentApplier
	{
		private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

		private static UrpEnvironmentApplier _instance;

		// the scene's sky as it was before it got scaled; null while it is unscaled
		private Material _source;
		private Material _scaled;
		private float _exposure;
		private float _reflectionIntensity;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register()
		{
			_instance?.Restore(false);
			_instance = new UrpEnvironmentApplier();
			VpeEnvironment.Register(_instance);
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
			UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
#endif
		}

#if UNITY_EDITOR
		private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
		{
			if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) {
				_instance?.Restore(false);
			}
		}
#endif

		public void SetSkyIntensity(float multiplier)
		{
			// another sky since (a different scene, or one swapped in): what was kept belongs to the old one
			if (_source != null && RenderSettings.skybox != _scaled) {
				Forget();
			}
			if (Mathf.Approximately(multiplier, 1f)) {
				Restore(true);
				return;
			}
			if (_source == null && !Capture()) {
				return;
			}
			// the skybox shaders declare their exposure as a gamma value: scale what it is in linear space
			_scaled.SetFloat(ExposureId, Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(_exposure) * multiplier));
			RenderSettings.reflectionIntensity = _reflectionIntensity * multiplier;
			UpdateAmbient();
		}

		// The ambient light is computed from the skybox as it now is. Unity may drop a request while earlier ones
		// are still being worked on (a slider sends one per frame), so it is asked again once the changes have settled.
		private static void UpdateAmbient()
		{
			DynamicGI.UpdateEnvironment();
			if (!Application.isPlaying) {
				return;
			}
			if (_settle == null) {
				var go = new GameObject("VPE Environment Refresh");
				Object.DontDestroyOnLoad(go);
				_settle = go.AddComponent<AmbientSettle>();
			}
			_settle.Request();
		}

		private static AmbientSettle _settle;

		private sealed class AmbientSettle : MonoBehaviour
		{
			// after the last change: once the requests before it had time to finish, and once more for good measure
			private static readonly float[] Delays = { 0.25f, 1f };
			private float _changedAt;
			private int _done;

			public void Request()
			{
				_changedAt = Time.unscaledTime;
				_done = 0;
				enabled = true;
			}

			private void Update()
			{
				if (Time.unscaledTime - _changedAt < Delays[_done]) {
					return;
				}
				DynamicGI.UpdateEnvironment();
				if (++_done >= Delays.Length) {
					enabled = false;
				}
			}
		}

		public void ConfigureLight(Light light, in VpeEnvironmentLight spec)
		{
			if (light.GetComponent<UniversalAdditionalLightData>() == null) {
				light.gameObject.AddComponent<UniversalAdditionalLightData>();
			}
			light.type = spec.Directional ? LightType.Directional : LightType.Spot;
			light.useColorTemperature = false;
			light.color = spec.Color;
			light.intensity = spec.Intensity * UrpPhysicalUnits.LightScale;
			light.shadows = spec.Shadows ? LightShadows.Soft : LightShadows.None;
			if (!spec.Directional) {
				light.spotAngle = spec.SpotAngle;
				light.innerSpotAngle = spec.SpotAngle * spec.InnerSpotPercent / 100f;
				light.range = spec.Range;
			}
		}

		// Scales a copy of the skybox material, so the project's own stays as it is.
		private bool Capture()
		{
			var sky = RenderSettings.skybox;
			if (sky == null || !sky.HasProperty(ExposureId)) {
				return false;
			}
			_source = sky;
			_scaled = new Material(sky) { name = sky.name + " (scaled)", hideFlags = HideFlags.HideAndDontSave };
			_exposure = sky.GetFloat(ExposureId);
			_reflectionIntensity = RenderSettings.reflectionIntensity;
			RenderSettings.skybox = _scaled;
			return true;
		}

		// settle: ask for the ambient light again once things have settled, which a teardown has no time for
		private void Restore(bool settle)
		{
			if (_source == null) {
				return;
			}
			if (RenderSettings.skybox == _scaled) {
				RenderSettings.skybox = _source;
				RenderSettings.reflectionIntensity = _reflectionIntensity;
				if (settle) {
					UpdateAmbient();
				} else {
					DynamicGI.UpdateEnvironment();
				}
			}
			Forget();
		}

		private void Forget()
		{
			if (_scaled != null) {
				if (Application.isPlaying) {
					Object.Destroy(_scaled);
				} else {
					Object.DestroyImmediate(_scaled);
				}
			}
			_source = null;
			_scaled = null;
		}
	}
}
