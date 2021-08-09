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

using System;
using UnityEngine;
using VisualPinball.Engine.VPT;

namespace VisualPinball.Unity.Urp
{
	public class PrefabProvider : IPrefabProvider
	{
		public GameObject CreateBumper()
		{
			return UnityEngine.Resources.Load<GameObject>("Prefabs/Bumper");
		}

		public GameObject CreateGate(int type)
		{
			switch (type) {
				case GateType.GateLongPlate:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Gate - Long Plate");
				case GateType.GatePlate:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Gate - Plate");
				case GateType.GateWireRectangle:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Gate - Wire Rectangle");
				case GateType.GateWireW:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Gate - Wire W");
				default:
					throw new ArgumentException(nameof(type), $"Unknown gate type {type}.");
			}
		}

		public GameObject CreateTarget(int type)
		{
			switch (type)
			{
				case TargetType.DropTargetBeveled:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Drop Target - Beveled");
				case TargetType.DropTargetFlatSimple:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Drop Target - Simple Flat");
				case TargetType.DropTargetSimple:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Drop Target - Simple");
				case TargetType.HitFatTargetRectangle:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Hit Target - Rectangle Fat");
				case TargetType.HitFatTargetSlim:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Hit Target - Rectangle Fat Narrow");
				case TargetType.HitFatTargetSquare:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Hit Target - Square Fat");
				case TargetType.HitTargetRectangle:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Hit Target - Rectangle");
				case TargetType.HitTargetRound:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Hit Target - Round");
				case TargetType.HitTargetSlim:
					return UnityEngine.Resources.Load<GameObject>("Prefabs/Hit Target - Narrow");
				default:
					throw new ArgumentException(nameof(type), $"Unknown target type {type}.");
			}
		}
	}
}
