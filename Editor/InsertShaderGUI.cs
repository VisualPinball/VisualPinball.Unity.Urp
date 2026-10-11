// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VisualPinball.Unity.Editor;

namespace VisualPinball.Unity.Urp
{
	/// <summary>Insert material controls without a dependency on HDRP's editor.</summary>
	public class InsertShaderGUI : ShaderGUI
	{
		public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
		{
			var cover = ((Material)materialEditor.target).shader.name == "Pinball/Insert Cover";
			if (cover) {
				EditorGUILayout.HelpBox("Slab refraction and transmission tint need Opaque Texture and Depth Texture, with the renderer's Depth Texture Mode set to After Opaques or Force Prepass. Use Opaque Downsampling: None for sharp detail. Without these buffers the cover renders its reflections, haze and normal detail.", MessageType.Info);
			}
			var locked = materialEditor.targets.OfType<Material>().Any(InsertColorPresets.IsReadOnly);
			using (new EditorGUI.DisabledScope(locked)) {
				foreach (var property in properties) {
					if ((property.propertyFlags & (ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData)) != 0) { continue; }
					// Presets own these controls in both pipelines. Edit the linked
					// preset through the Insert inspector, or unlink it there first.
					var owned = materialEditor.targets.OfType<Material>().Any(material =>
						InsertMaterialPresets.LinkedPreset(material)?.Find(property.name) != null
						|| InsertColorPresets.LinkedPreset(material) != null && IsColorProperty(property.name));
					using (new EditorGUI.DisabledScope(owned)) {
						materialEditor.ShaderProperty(property, property.displayName);
					}
				}
				materialEditor.RenderQueueField();
				materialEditor.EnableInstancingField();
				materialEditor.DoubleSidedGIField();
			}
		}

		private static bool IsColorProperty(string name) => name is "_BaseColor" or "_InsertCavityColor" or "_EmissiveColor" or "_InsertLuminosity" or "_HazeColor" or "_CoverTint";
	}
}
