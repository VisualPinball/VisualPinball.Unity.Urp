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
	// those through exposure (signal = luminance / 2^EV100); URP has neither physical
	// units nor an exposure system, so this is the single place where physical values are mapped
	// into URP's unitless lighting.
	public static class UrpPhysicalUnits
	{
		// The exposure baked into the conversion: the one the HDRP player renders with. Its game camera
		// exposes physically (Exposure "Use Physical Camera"), at f/5.6, 1/50 s and ISO 1100, so
		// EV100 = log2(N² / t · 100 / ISO) ≈ 7.16. Keep this in sync with that camera, or the URP player
		// drifts brighter or darker than the HDRP one.
		public static readonly float ReferenceEv100 = Ev100(5.6f, 1f / 50f, 1100f);

		// Extra stops applied to emissive surfaces on top of ReferenceEv100, for differences in how
		// the two pipelines' bloom picks up emissive HDR values.
		public const float EmissiveExtraEv = 0f;

		// Measured, not derived: with the exposure above, the URP player still rendered the same table about
		// 0.68 EV darker than the HDRP player (A/B of the dark and the lamps-on table, 2026-10-16). Recalibrate
		// it with the VPE > Look Compare tools in both players when lights, materials or post-processing change.
		public const float CalibrationEv = 0.68f;

		// The multiplier HDRP's exposure applies to a luminance (nits), with the calibration. The HDRP player's
		// pipeline uses a perfect lens (Lens Attenuation 0.78), which leaves out the usual 1.2 lens factor.
		public static readonly float ExposureScale = Mathf.Pow(2f, CalibrationEv - ReferenceEv100);

		// nits (emissive luminance) → URP emission value.
		public static readonly float EmissiveScale = ExposureScale / Mathf.Pow(2f, EmissiveExtraEv);

		// candela / lux → URP light intensity. URP's Lambert term carries no 1/π (the legacy
		// Unity convention bakes it into the light), so physical light intensities additionally
		// divide by π. Derived from ReferenceEv100 alone — EmissiveExtraEv does not apply here.
		public static readonly float LightScale = ExposureScale / Mathf.PI;

		// HDRP spreads each lamp's light through SSGI (screen-space bounce); URP has no GI, so
		// table lamps only produce their direct pools. Widening the authored ranges makes the
		// overlapping pools stand in for the first bounce — and unlike a static ambient lift it
		// follows the lamp state (GI string off → playfield goes dark, as in HDRP).
		public const float LightRangeMultiplier = 3.5f;

		/// <summary>The exposure value at ISO 100 of a camera with the given aperture (f-number), shutter time (s) and ISO.</summary>
		public static float Ev100(float aperture, float shutterSeconds, float iso)
			=> Mathf.Log(aperture * aperture / shutterSeconds * 100f / iso, 2f);

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
