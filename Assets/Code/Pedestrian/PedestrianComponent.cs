using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OSMTrafficSim
{
    [Serializable]
    public struct PedestrianData : IComponentData
    {
        public float2 LocalPos;
        public float3 WorldPos;
        public float3 Forword;
        public float Speed;
        public int2 GridId;
    }

    public class PedestrianComponent : ComponentDataWrapper<PedestrianData>
    {
    }
}
