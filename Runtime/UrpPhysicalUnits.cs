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
		// Reference exposure baked into the conversion. Calibrated 2026-07-02 by A/B region-stats
		// against the HDRP player under the neutral wooden_studio_17_1k HDRI (same camera pose,
		// only table lights on): EV 4.5 brings GI-lamp pools and faux-bulb cores in line with
		// HDRP's histogram exposure. Tune this one value if overall URP lamp/emissive brightness
		// drifts from the HDRP player.
		public const float ReferenceEv100 = 5f;

		// Extra stops applied to emissive surfaces on top of ReferenceEv100. A/B stats showed
		// faux-bulb cores/halos still ~1 EV hotter than HDRP when lights already matched —
		// URP's bloom picks emissive HDR values up more aggressively than HDRP's.
		public const float EmissiveExtraEv = 1f;

		// nits (emissive luminance) → URP emission value.
		public static readonly float EmissiveScale = 1f / (1.2f * Mathf.Pow(2f, ReferenceEv100 + EmissiveExtraEv));

		// candela / lux → URP light intensity. URP's Lambert term carries no 1/π (the legacy
		// Unity convention bakes it into the light), so physical light intensities additionally
		// divide by π. Derived from ReferenceEv100 alone — EmissiveExtraEv does not apply here.
		public static readonly float LightScale = 1f / (1.2f * Mathf.Pow(2f, ReferenceEv100) * Mathf.PI);

		// HDRP spreads each lamp's light through SSGI (screen-space bounce); URP has no GI, so
		// table lamps only produce their direct pools. Widening the authored ranges makes the
		// overlapping pools stand in for the first bounce — and unlike a static ambient lift it
		// follows the lamp state (GI string off → playfield goes dark, as in HDRP).
		public const float LightRangeMultiplier = 3.5f;

		// Registered with VpeLightUnitAdjuster at bootstrap; runs after each restored light
		// profile has been applied with its authored (HDRP-physical) values.
		public static void AdjustRestoredLight(Light light)
		{
			if (!light) {
				return;
			}
			light.intensity *= LightScale;
			if (light.type is LightType.Point or LightType.Spot) {
				light.range *= LightRangeMultiplier;
			}
		}
	}
}
