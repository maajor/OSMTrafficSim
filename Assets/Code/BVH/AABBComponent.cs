using System;
using Unity.Entities;
using Unity.Mathematics;

namespace OSMTrafficSim.BVH
{
    [Serializable]
    public struct BVHAABB : IComponentData
    {
        public float3 Min;
        public float3 Max;
    }

    public class AABBComponent : ComponentDataProxy<BVHAABB> { }
}