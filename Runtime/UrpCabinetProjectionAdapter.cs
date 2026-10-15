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
using UnityEngine.Rendering.Universal;
using VisualPinball.Unity;

namespace VisualPinball.Unity.Urp
{
	/// <summary>
	/// Lets URP render a camera through a cabinet projection (an off-centre frustum).
	/// </summary>
	///
	/// <remarks>
	/// URP passes the camera's projection matrix on as is and builds TAA's jitter as a clip-space
	/// translation, so an off-centre frustum needs no fallback. The camera's aspect stays the one of its
	/// output; the temporal history is reset when the view jumps.
	/// </remarks>
	public sealed class UrpCabinetProjectionAdapter : ICabinetProjectionAdapter
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register() => CabinetProjection.Register(new UrpCabinetProjectionAdapter());

		public void Attach(Camera camera)
		{
			UrpGraphicsApplier.ApplyCameraAntialiasing(camera);
		}

		public void ApplyMetadata(Camera camera, in CabinetProjectionResult projection)
		{
			camera.ResetAspect();
			camera.fieldOfView = projection.EnclosingFieldOfView;
		}

		public void ProjectionChanged(Camera camera, in CabinetProjectionResult projection, bool discontinuous)
		{
			if (discontinuous) {
				ResetHistory(camera);
			}
		}

		public void Detach(Camera camera)
		{
			if (camera == null) {
				return;
			}
			ResetHistory(camera);
			UrpGraphicsApplier.ApplyCameraAntialiasing(camera);
		}

		public VpeAntiAliasing EffectiveAntiAliasing(VpeAntiAliasing requested, out string reason)
		{
			reason = null;
			return requested;
		}

		// once, before the next render: resets TAA history, motion vectors and the jitter phase
		private static void ResetHistory(Camera camera)
		{
			if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) {
				data.resetHistory = true;
			}
		}
	}
}
