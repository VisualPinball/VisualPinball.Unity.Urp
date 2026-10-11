// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
// SPDX-License-Identifier: GPL-3.0-or-later

using UnityEditor;

namespace VisualPinball.Unity.Urp
{
	[InitializeOnLoad]
	internal static class UrpInsertRenderingEditor
	{
		static UrpInsertRenderingEditor() => UrpInsertRendering.Register();
	}
}
