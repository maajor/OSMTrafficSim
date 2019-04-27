using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace OSMTrafficSim.BVH
{
    [Serializable]
    public struct InstanceRendererProperty : IComponentData
    {
        public int ParamId;
        public float4 Value;
    }

    public class InstanceRendererPropertyComponent : ComponentDataProxy<InstanceRendererProperty>
    {
    }
}
