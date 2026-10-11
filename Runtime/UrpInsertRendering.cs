// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
// SPDX-License-Identifier: GPL-3.0-or-later

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VisualPinball.Unity.Urp
{
	/// <summary>Enables cover refraction only when this camera supplies both scene buffers.</summary>
	public static class UrpInsertRendering
	{
		private static readonly int SceneColorAvailable = Shader.PropertyToID("_VpeInsertSceneColorAvailable");

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		public static void Register()
		{
			RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
			RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
			Shader.SetGlobalFloat(SceneColorAvailable, 0f);
		}

		private static void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			var available = false;
			if (pipeline != null) {
				var color = pipeline.supportsCameraOpaqueTexture;
				var depth = pipeline.supportsCameraDepthTexture;
				var renderer = pipeline.scriptableRenderer;
				if ((camera.cameraType is CameraType.Game or CameraType.VR) && camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) {
					renderer = data.scriptableRenderer;
					color = data.requiresColorTexture;
					depth = data.requiresDepthTexture;
					// Overlay cameras do not produce their own opaque-color copy.
					color &= data.renderType == CameraRenderType.Base;
				}
				// Depth requested by the camera may only be copied after transparents.
				// Match the selected renderer through URP's public lists; camera renderer
				// indices are internal, and the default need not be the first renderer.
				var renderers = pipeline.renderers;
				var settings = pipeline.rendererDataList;
				for (var i = 0; i < renderers.Length && i < settings.Length; i++) {
					if (renderers[i] != renderer) { continue; }
					available = color && depth && settings[i] is UniversalRendererData universal
						&& universal.copyDepthMode != CopyDepthMode.AfterTransparents;
					break;
				}
			}
			Shader.SetGlobalFloat(SceneColorAvailable, available ? 1f : 0f);
		}
	}
}
