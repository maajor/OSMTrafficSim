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
    struct BitonicMergeJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> values;
        [NativeDisableParallelForRestriction]
        public NativeArray<int> indexConverter;

        public int strideSwap;
        public int strideRisingGroup;
        public void Execute(int index)
        {
            int swapPairId = index / strideSwap;
            int swapGroupId = index - swapPairId * strideSwap;
            int swapGroupStartId = swapPairId * 2 * strideSwap;
            int swapIdFirst = swapGroupStartId + swapGroupId;
            int swapIdSecond = swapIdFirst + strideSwap;
            int risingGroupId = swapPairId / strideRisingGroup;
            bool rising = risingGroupId % 2 == 0 ? true : false;
            if (values[swapIdFirst] > values[swapIdSecond] == rising)
            {
                uint tempValue = values[swapIdFirst];
                int tempId = indexConverter[swapIdFirst];
                values[swapIdFirst] = values[swapIdSecond];
                indexConverter[swapIdFirst] = indexConverter[swapIdSecond];
                values[swapIdSecond] = tempValue;
                indexConverter[swapIdSecond] = tempId;
            }
        }
    }

    [BurstCompile]
    struct BitonicSortJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> values;
        [NativeDisableParallelForRestriction]
        public NativeArray<int> indexConverter;

        public int strideSwap;

        public void Execute(int index)
        {
            int swapPairId = index / strideSwap;
            int swapGroupId = index - swapPairId * strideSwap;
            int swapGroupStartId = swapPairId * 2 * strideSwap;
            int swapIdFirst = swapGroupStartId + swapGroupId;
            int swapIdSecond = swapIdFirst + strideSwap;
            if (values[swapIdFirst] > values[swapIdSecond])
            {
                uint tempValue = values[swapIdFirst];
                int tempId = indexConverter[swapIdFirst];
                values[swapIdFirst] = values[swapIdSecond];
                indexConverter[swapIdFirst] = indexConverter[swapIdSecond];
                values[swapIdSecond] = tempValue;
                indexConverter[swapIdSecond] = tempId;
            }
        }
    }
}