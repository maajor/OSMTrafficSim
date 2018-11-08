using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace OSMTrafficSim
{
    [Serializable]
    public struct AABB : IComponentData
    {
        public float3 Min;
        public float3 Max;
    }

    public class AABBComponent : ComponentDataWrapper<AABB> { }

    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct BVHNode
    {
        public AABB aabb;
        public int EntityId;
        public int LeftNodeIndex;
        public int RightNodeIndex;
        public int ParentNodeIndex;
        public byte IsValid;
    }
}