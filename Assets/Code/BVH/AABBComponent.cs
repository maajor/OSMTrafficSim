using System;
using Unity.Entities;
using Unity.Mathematics;

namespace OSMTrafficSim.BVH
{
    [Serializable]
    public struct AABB : IComponentData
    {
        public float3 Min;
        public float3 Max;
    }

    public class AABBComponent : ComponentDataWrapper<AABB> { }
}