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
    public struct CalculateBoundsJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<BVHAABB> results;

        [NativeDisableParallelForRestriction]
        [ReadOnly]
        public ComponentDataArray<BVHAABB> AABB;

        public int batchSize;

        public void Execute(int i)
        {
            int start = i * batchSize;
            int end = (i + 1) * batchSize > AABB.Length ? AABB.Length : (i + 1) * batchSize;
            BVHAABB startAABB = AABB[start];
            for (int k = start + 1; k < end; k++)
            {
                startAABB = Utils.GetEncompassingAABB(startAABB, AABB[k]);
            }
            results[i] = startAABB;
        }
    }

    [BurstCompile]
    public struct CalculateBoundsMergedJob : IJob
    {
        public NativeArray<BVHAABB> results;

        public void Execute()
        {
            BVHAABB startAABB = results[0];

            for (int i = 1; i < results.Length; i++)
            {
                startAABB = Utils.GetEncompassingAABB(startAABB, results[i]);
            }

            results[0] = startAABB;
        }
    }
}
