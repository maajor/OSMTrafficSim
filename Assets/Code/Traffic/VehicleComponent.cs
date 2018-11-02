using System.Collections;
using System.Collections.Generic;
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

namespace OSMTrafficSim
{
    [Serializable]
    public struct VehicleData : IComponentData
    {
        public uint id;

        public VehicleData(uint id) {
            this.id = id;
        }
    }

    [RequireComponent(typeof(CopyTransformToGameObjectComponent))]
    public class VehicleComponent : ComponentDataWrapper<VehicleData> {

        public VehicleComponent(uint id) {
            Value = new VehicleData(id);
        }
    }
}
