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
    public struct VehicleTemplate : ISharedComponentData
    {
        public float Weight;
        public Vector3 ColliderCenter;
        public Vector3 ColliderSize;
        public MeshInstanceRenderer renderer;
    }

    public class VehicleTemplateComponent : SharedComponentDataWrapper<VehicleTemplate>
    {
    }
}