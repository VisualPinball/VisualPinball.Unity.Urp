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
	// .vpe tables are authored under HDRP in physical units: emissives in nits, punctual lights
	// in candela (Light.intensity holds the candela value), directionals in lux. HDRP tone-maps
	// those through exposure (signal = luminance / (1.2 * 2^EV100)); URP has neither physical
	// units nor an exposure system, so this is the single place where physical values are mapped
	// into URP's unitless lighting.
	public static class UrpPhysicalUnits
	{
		// Reference exposure baked into the conversion. Calibrated against the HDRP-rendered
		// lights-on package screenshots (T2): around EV 3–4, faux bulbs read blown-white with
		// bloom (as in HDRP) and GI lamps light the playfield warmly without washing the paint
		// out. Tune this one value if overall URP brightness drifts from the HDRP player.
		public const float ReferenceEv100 = 3.5f;

		// nits (emissive luminance) → URP emission value.
		public static readonly float EmissiveScale = 1f / (1.2f * Mathf.Pow(2f, ReferenceEv100));

		// candela / lux → URP light intensity. URP's Lambert term carries no 1/π (the legacy
		// Unity convention bakes it into the light), so physical light intensities additionally
		// divide by π.
		public static readonly float LightScale = EmissiveScale / Mathf.PI;

		// Registered with VpeLightUnitAdjuster at bootstrap; runs after each restored light
		// profile has been applied with its authored (HDRP-physical) values.
		public static void AdjustRestoredLight(Light light)
		{
			if (!light) {
				return;
			}
			light.intensity *= LightScale;
		}
	}
}
