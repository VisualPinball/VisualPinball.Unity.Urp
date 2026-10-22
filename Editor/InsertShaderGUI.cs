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
					if (InsertTracedFields.Owns(property.name)) { continue; }
					using (new EditorGUI.DisabledScope(Owned(materialEditor, property.name))) {
						materialEditor.ShaderProperty(property, property.displayName);
					}
				}
				DrawTraced(materialEditor, properties);
				materialEditor.RenderQueueField();
				materialEditor.EnableInstancingField();
				materialEditor.DoubleSidedGIField();
			}
		}

		/// <summary>
		/// The opt-in Traced mode in a section of its own, its controls shown
		/// only while it's on, as in HDRP's inspector.
		/// </summary>
		private static void DrawTraced(MaterialEditor materialEditor, MaterialProperty[] properties)
		{
			var keyword = FindProperty(InsertTracedFields.Keyword, properties, false);
			if (keyword == null) {
				return;
			}
			// A plain foldout in the header's style: a foldout header group throws when the
			// material is drawn inside another one.
			var open = EditorGUILayout.Foldout(SessionState.GetBool(TracedFoldoutKey, true), InsertTracedFields.Title, true, EditorStyles.foldoutHeader);
			SessionState.SetBool(TracedFoldoutKey, open);
			if (open) {
				materialEditor.ShaderProperty(keyword, InsertTracedFields.KeywordLabel);
				if (materialEditor.targets.OfType<Material>().All(material => material.IsKeywordEnabled(InsertTracedFields.Keyword))) {
					EditorGUILayout.HelpBox(InsertTracedFields.Note, MessageType.None);
					foreach (var section in InsertTracedFields.Sections) {
						EditorGUILayout.Space(4f);
						EditorGUILayout.LabelField(section.Title, EditorStyles.boldLabel);
						foreach (var entry in section.Entries) {
							var property = FindProperty(entry.Name, properties, false);
							if (property == null) {
								continue;
							}
							using (new EditorGUI.DisabledScope(Owned(materialEditor, entry.Name))) {
								if (entry.IsTexture) {
									materialEditor.TexturePropertySingleLine(entry.Label, property);
								} else {
									materialEditor.ShaderProperty(property, entry.Label);
								}
							}
						}
					}
				}
			}
			EditorGUILayout.Space(6f);
		}

		private const string TracedFoldoutKey = "VisualPinball.Urp.InsertShaderGUI.Traced";

		/// <summary>
		/// Presets own these controls in both pipelines. Edit the linked
		/// preset through the Insert inspector, or unlink it there first.
		/// </summary>
		private static bool Owned(MaterialEditor materialEditor, string name) => materialEditor.targets.OfType<Material>().Any(material =>
			InsertMaterialPresets.LinkedPreset(material)?.Find(name) != null
			|| InsertColorPresets.LinkedPreset(material) != null && IsColorProperty(name));

		private static bool IsColorProperty(string name) => name is "_BaseColor" or "_InsertCavityColor" or "_EmissiveColor" or "_InsertLuminosity" or "_HazeColor" or "_CoverTint";
	}
}
