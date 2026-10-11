// Visual Pinball Engine
// Copyright (C) 2020 freezy and VPE Team
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

// ReSharper disable UnusedType.Global
// ReSharper disable CheckNamespace

using UnityEngine;
using UnityEngine.Rendering;
using VisualPinball.Engine.VPT.Light;
using Light = UnityEngine.Light;

namespace VisualPinball.Unity.Urp
{
	public class LightConverter : ILightConverter
	{
		public void UpdateLight(Light light, LightData data, bool isInsert)
		{
			// Set color and position
			light.color = data.Color2.ToUnityColor();
			light.intensity = data.Intensity / 100f;
			light.range = data.Falloff * 0.001f;
			// TODO: vpe specific data for height
			light.transform.localPosition = new Vector3(0f, 0f, 25f);

			// TODO: vpe specific shadow settings
			light.shadows = LightShadows.None;
			light.shadowBias = 0f;
			light.shadowNearPlane = 0.001f;
		}

		public void SetColor(Light light, Color color)
		{
			light.color = color;
		}

		public void SetShadow(Light light, bool enabled, bool isDynamic, float nearPlane = 0.01f)
		{
			// light.shadows = enabled ? LightShadows.Soft : LightShadows.None;
			// light.shadowNearPlane = nearPlane;
		}
		public void SetRange(Light light, float range)
		{
			light.range = light.type is LightType.Point or LightType.Spot ? range * UrpPhysicalUnits.LightRangeMultiplier : range;
		}

		public void SetIntensity(Light light, float intensityLumen)
		{
			// Match restored physical lights: convert flux to candela first,
			// then apply the same reference exposure and Lambert conversion.
			var solidAngle = light.type == LightType.Spot && light.enableSpotReflector
				? LightUnitUtils.GetSolidAngleFromSpotLight(light.spotAngle) : LightUnitUtils.SphereSolidAngle;
			light.intensity = LightUnitUtils.LumenToCandela(Mathf.Max(0f, intensityLumen), Mathf.Max(solidAngle, 1e-6f)) * UrpPhysicalUnits.LightScale;
		}


		public void SetTemperature(Light light, float temperature)
		{
			// todo
		}

		public void SpotLight(Light light, float outer, float innerPercent)
		{
			light.spotAngle = outer;
			light.innerSpotAngle = outer * innerPercent * 0.01f;
		}

		public void PyramidAngle(Light light, float angle, float aspectRatio)
		{
			// urp has no pyramid spot light
		}
	}
}
