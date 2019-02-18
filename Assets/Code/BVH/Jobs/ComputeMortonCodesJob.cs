using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.Experimental;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace OSMTrafficSim.BVH
{

    [BurstCompile]
    struct ComputeMortonCodesJob : IJobParallelFor
    {
        [ReadOnly] public ComponentDataArray<BVHAABB> aabbs;
        
        [WriteOnly]
        public NativeArray<uint> mortonCodes;
        [WriteOnly]
        public NativeArray<int> indexConverter;

        [ReadOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<BVHAABB> Bounds;

        public void Execute(int i)
        {
            indexConverter[i] = i;
            if (i > aabbs.Length - 1)
            {
                mortonCodes[i] = uint.MaxValue;
                return;
            }
            float3 center = Utils.GetAABBCenter(aabbs[i]);
            float3 normalized = (center - Bounds[0].Min) / (Bounds[0].Max - Bounds[0].Min);
            //2D enough
            mortonCodes[i] = Utils.CalculateMortonCode2D(normalized);
            //mortonCodes[i] = Utils.CalculateMortonCode(normalized);
        }
    }
}