using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{
    [Serializable]
    public struct InstanceRendererData : ISharedComponentData
    {
        public Mesh Mesh;
        public Material Material;
        public int SubMesh;

        public ShadowCastingMode CastShadows;
        public bool ReceiveShadows;
        public float CullDistance;
        public int InstanceShaderPropertyId;
    }

    public class InstanceRendererComponent : SharedComponentDataWrapper<InstanceRendererData>
    {
    }
}
