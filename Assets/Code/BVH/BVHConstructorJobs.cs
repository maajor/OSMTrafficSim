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

namespace OSMTrafficSim
{
    [BurstCompile]
    struct ResetBVH : IJobParallelFor
    {
        public NativeArray<BVHNode> BVHArray;

        public void Execute(int i)
        {
            BVHNode bvhNode = BVHArray[i];
            bvhNode.IsValid = 0;
            bvhNode.ParentNodeIndex = -1;
            bvhNode.aabb = new AABB();
            BVHArray[i] = bvhNode;
        }
    }

    [BurstCompile]
    struct ResetLocks : IJobParallelFor
    {
        //[NativeDisableUnsafePtrRestriction]
        //public long* locks;

        public void Execute(int i)
        {
            //locks[i] = 0;
        }
    }

    [BurstCompile]
    struct ConstructBVHChildNodes : IJobParallelFor
    {
        [ReadOnly] public ComponentDataArray<AABB> AABB;
        [ReadOnly] public NativeArray<int> indexConverter;

        [NativeDisableParallelForRestriction]
        public NativeArray<BVHNode> BVHArray;

        public void Execute(int i)
        {
            int halfLength = BVHArray.Length / 2;
            int entityindex = indexConverter[i];
            int bvhIndex = halfLength + i;
            BVHNode bvhNode = BVHArray[bvhIndex];
            bvhNode.aabb = AABB[entityindex];
            bvhNode.EntityId = entityindex;
            bvhNode.RightNodeIndex = bvhIndex;
            bvhNode.LeftNodeIndex = -1;
            bvhNode.IsValid = 2;
            BVHArray[bvhIndex] = bvhNode;
        }
    }


    [BurstCompile]
    struct ComputeAndSortMortonCodes : IJob
    {
        [ReadOnly]
        public ComponentDataArray<AABB> aabbs;
        public NativeArray<int> mortonCodes;
        public NativeArray<int> mortonCodesTemp;
        public NativeArray<int> indexConverter;
        public NativeArray<int> indexConverterTemp;
        public NativeArray<int> radixSortBitValues;
        public NativeArray<int> radixSortOffsets;
        public NativeArray<int> sortResultsArrayIsA;

        private int zeroesHistogramCounter;
        private int onesHistogramCounter;
        private int zeroesPrefixSum;
        private int onesPrefixSum;

        public AABB Bound;

        public void Execute()
        {
            // Calculate all morton codes and init sorted index map
            for (int i = 0; i < aabbs.Length; i++)
            {
                float3 center = Utils.GetAABBCenter(aabbs[i]);
                float3 normalized = (center - Bound.Min) / (Bound.Max - Bound.Min);
                mortonCodes[i] = Utils.CalculateMortonCode(normalized);

                indexConverter[i] = i;
                indexConverterTemp[i] = i;
            }

            // Radix sort ascending
            for (int bitPosition = 0; bitPosition < 31; bitPosition++)
            {
                bool isEvenIteration = bitPosition % 2 == 0;

                // init histogram counts
                zeroesHistogramCounter = 0;
                onesHistogramCounter = 0;
                zeroesPrefixSum = 0;
                onesPrefixSum = 0;

                // Compute histograms and offsets
                for (int i = 0; i < aabbs.Length; i++)
                {
                    int bitVal = 0;
                    if (isEvenIteration)
                    {
                        bitVal = (mortonCodes[i] & (1 << bitPosition)) >> bitPosition;
                    }
                    else
                    {
                        bitVal = (mortonCodesTemp[i] & (1 << bitPosition)) >> bitPosition;
                    }

                    radixSortBitValues[i] = bitVal;

                    if (bitVal == 0)
                    {
                        radixSortOffsets[i] = zeroesHistogramCounter;
                        zeroesHistogramCounter += 1;
                    }
                    else
                    {
                        radixSortOffsets[i] = onesHistogramCounter;
                        onesHistogramCounter += 1;
                    }
                }

                // calc prefix sum from histogram
                zeroesPrefixSum = 0;
                onesPrefixSum = zeroesHistogramCounter;

                // Reorder array
                for (int i = 0; i < aabbs.Length; i++)
                {
                    int newIndex = 0;
                    if (radixSortBitValues[i] == 0)
                    {
                        newIndex = zeroesPrefixSum + radixSortOffsets[i];
                    }
                    else
                    {
                        newIndex = onesPrefixSum + radixSortOffsets[i];
                    }

                    if (isEvenIteration)
                    {
                        mortonCodesTemp[newIndex] = mortonCodes[i];
                        indexConverterTemp[newIndex] = indexConverter[i];
                    }
                    else
                    {
                        mortonCodes[newIndex] = mortonCodesTemp[i];
                        indexConverter[newIndex] = indexConverterTemp[i];
                    }
                }

                sortResultsArrayIsA[0] = isEvenIteration ? 0 : 1; // it's the A array only for odd number iterations
            }

            if (sortResultsArrayIsA[0] == 0)
            {
                for (int i = 0; i < aabbs.Length; i++)
                {
                    mortonCodes[i] = mortonCodesTemp[i];
                    indexConverter[i] = indexConverterTemp[i];
                }
            }

        }
    }

    //https://devblogs.nvidia.com/thinking-parallel-part-iii-tree-construction-gpu/
    [BurstCompile]
    struct ConstructBVHInternalNodes : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> mortonCodes;
        
        [WriteOnly]
        public NativeArray<BVHNode> BVHArray;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> ParentIndex;

        public int NumObjects;

        int2 determineRange(int i)
        {
            int d = (clz_safe(i, i + 1) - clz_safe(i, i - 1)) > 0 ? 1 : -1;
            int commonPrefixMin = clz_safe(i, i - d);
            int l_max = 2;
            while (clz_safe(i, i + d * l_max) > commonPrefixMin)
            {
                l_max *= 2;
            }
            int l = 0;
            int t = l_max;
            do
            {
                t = (t + 1) >> 1; // exponential decrease
                if (clz_safe(i, i + d * (l + t)) > commonPrefixMin)
                {
                    l += t;
                }
            }
            while (t > 1);

            int j = i + l * d;
            int2 range = d > 0 ? new int2(i, j) : new int2(j, i);
            return range;
        }

        int findSplit(int first, int last)
        {
            // Identical Morton codes => split the range in the middle.

            //int firstCode = mortonCodes[first];
            //int lastCode = mortonCodes[last];

            //if (firstCode == lastCode)
            //    return (first + last) >> 1;

            // Calculate the number of highest bits that are the same
            // for all objects, using the count-leading-zeros intrinsic.

            int commonPrefix = clz_index(first, last);

            // Use binary search to find where the next bit differs.
            // Specifically, we are looking for the highest object that
            // shares more than commonPrefix bits with the first one.

            int split = first; // initial guess
            int step = last - first;

            do
            {
                step = (step + 1) >> 1; // exponential decrease
                int newSplit = split + step; // proposed new position

                if (newSplit < last)
                {
                    int splitPrefix = clz_index(first, newSplit);
                    if (splitPrefix > commonPrefix)
                        split = newSplit; // accept proposal
                }
            }
            while (step > 1);

            return split;
        }

        private int clz_safe(int idx, int idy)
        {
            if (idy < 0 || idy > NumObjects - 1) return -1;
            return clz_index(idx, idy);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int clz_index(int idx, int idy)
        {
            //rely on morton being unique, otherwise clz its index
            //https://devblogs.nvidia.com/parallelforall/wp-content/uploads/2012/11/karras2012hpg_paper.pdf
            //see section 4. BVHs, Octrees, and k-d Trees
            return mortonCodes[idx] == mortonCodes[idy] ? (NumObjects - math.max(idx, idy)) + 32 : clz_value(mortonCodes[idx], mortonCodes[idy]);
        }

        //https://stackoverflow.com/questions/10439242/count-leading-zeroes-in-an-int32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int clz_value(int value1, int value2)
        {
            int value = value1 ^ value2;
            //do the smearing
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            //count the ones
            value -= ((value >> 1) & 0x55555555);
            value = (((value >> 2) & 0x33333333) + (value & 0x33333333));
            value = (((value >> 4) + value) & 0x0f0f0f0f);
            value += (value >> 8);
            value += (value >> 16);
            return 32 - (value & 0x0000003f);
        }

        public void Execute(int index)
        {
            /*if (index == 200 || index == 199 || index == 201 || index == 202 || index == 203)
            {
                int kk = 0;
            }*/
            int halfBVHSize = BVHArray.Length / 2;
            int2 range = determineRange(index);
            int first = range.x;
            int last = range.y;
            

            // Determine where to split the range.

            int split = findSplit(first, last);

            int childAIndex = split == first ? split + halfBVHSize : split;
            int childBIndex = (split + 1) == last ? split + halfBVHSize + 1 : split + 1;
            
            BVHNode thisnode = new BVHNode();
            thisnode.RightNodeIndex = childBIndex;
            thisnode.LeftNodeIndex = childAIndex;
            thisnode.EntityId = -1;
            thisnode.IsValid = 0;//No AABB Updated
            BVHArray[index] = thisnode;

            ParentIndex[childAIndex] = index;
            ParentIndex[childBIndex] = index;
        }
    }

    [BurstCompile]
    struct UpdateBVHParentIndex : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> ParentIndex;
        
        public NativeArray<BVHNode> BVHArray;

        public void Execute(int i)
        {
            BVHNode node = BVHArray[i];
            node.ParentNodeIndex = i == 0 ? -1 : ParentIndex[i];
            BVHArray[i] = node;
        }
    }

    [BurstCompile]
    struct UpdateAABB : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<BVHNode> BVHArray;
        
        
        public void Execute(int i)
        {
            
            int halfLength = BVHArray.Length / 2;
            int leafNodeId = halfLength + i;
            AABB leafNodeAABB = BVHArray[leafNodeId].aabb;
            int parentIndex = BVHArray[leafNodeId].ParentNodeIndex;
            int wait = 0;
            while (parentIndex != -1)
            {

                BVHNode parent = BVHArray[parentIndex];
                if (parent.IsValid < 1)
                {
                    parent.aabb = leafNodeAABB;
                    parent.IsValid = 1;
                    BVHArray[parentIndex] = parent;
                    break;
                }
                else
                {
                    parent.aabb = Utils.GetEncompassingAABB(parent.aabb, leafNodeAABB);
                    parent.IsValid = 2;
                    BVHArray[parentIndex] = parent;
                }
                leafNodeAABB = parent.aabb;
                parentIndex = parent.ParentNodeIndex;
            }

        }

        public void SerialExecute(int i)
        {
            int halfLength = BVHArray.Length / 2;
            int leafNodeId = halfLength + i;
            AABB leafNodeAABB = BVHArray[leafNodeId].aabb;
            int parentIndex = BVHArray[leafNodeId].ParentNodeIndex;
            int wait = 0;
            while (parentIndex != -1)
            {
                BVHNode parent = BVHArray[parentIndex];
                if (parent.IsValid == 0)
                {
                    parent.aabb = leafNodeAABB;
                    parent.IsValid = 1;
                    BVHArray[parentIndex] = parent;
                }
                parent.aabb = Utils.GetEncompassingAABB(parent.aabb, leafNodeAABB);
                parent.IsValid = 2;
                BVHArray[parentIndex] = parent;
                leafNodeAABB = parent.aabb;
                parentIndex = parent.ParentNodeIndex;
            }

        }
    }
}