using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
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

    public class AABBComponent : ComponentDataWrapper<AABB> {}

    public struct BVHNode
    {
        public AABB aabb;
        public int EntityId;
        public int LeftNodeIndex;
        public int RightNodeIndex;
        public int ParentNodeIndex;
        public byte IsValid;
    }

    public class BVHConstructor
    {

        public BVHConstructor(int capacity)
        {
            _capacity = capacity;
            mortonCodes = new NativeArray<int>(capacity, Allocator.Persistent);
            mortonCodesTemp = new NativeArray<int>(capacity, Allocator.Persistent);
            indexConverterTemp = new NativeArray<int>(capacity, Allocator.Persistent);
            indexConverter = new NativeArray<int>(capacity, Allocator.Persistent);
            radixSortBitValues = new NativeArray<int>(capacity, Allocator.Persistent);
            radixSortOffsets = new NativeArray<int>(capacity, Allocator.Persistent);
            sortResultsArrayIsA = new NativeArray<int>(1, Allocator.Persistent);

            int requiredBVHLength = (Utils.GetNextHighestPowerOf2(Utils.ceil_pow2(capacity) + 1)) - 1;

            BVHArray = new NativeArray<BVHNode>(requiredBVHLength, Allocator.Persistent);
        }

        public NativeArray<BVHNode> BVHArray;

        private int _capacity;
        private NativeArray<int> mortonCodes;
        private NativeArray<int> mortonCodesTemp;
        private NativeArray<int> indexConverterTemp;
        private NativeArray<int> indexConverter;
        private NativeArray<int> radixSortBitValues;
        private NativeArray<int> radixSortOffsets;
        private NativeArray<int> sortResultsArrayIsA;

        private AABB GetBoundingBox(ComponentDataArray<AABB> AABB)
        {
            Bounds bd = new Bounds();
            AABB result = AABB[0];
            for (int i = 0; i < _capacity; i++)
            {
                result.Min = math.min(result.Min, AABB[i].Min);
                result.Max = math.max(result.Max, AABB[i].Max);
            }

            return result;
        }

        public JobHandle Calculate(JobHandle deps, ComponentDataArray<AABB> AABB)
        {
            var bound = GetBoundingBox(AABB);

            var resetBVH = new ResetBVH()
            {
                BVHArray = BVHArray,
            };
            deps = resetBVH.Schedule(BVHArray.Length, 32, deps);

            var sortMortonCodes = new ComputeAndSortMortonCodes
            {
                aabbs = AABB,
                mortonCodes = mortonCodes,
                mortonCodesTemp = mortonCodesTemp,
                indexConverter = indexConverter,
                indexConverterTemp = indexConverterTemp,
                radixSortBitValues = radixSortBitValues,
                radixSortOffsets = radixSortOffsets,
                sortResultsArrayIsA = sortResultsArrayIsA,
                Bound = bound
            };
            deps = sortMortonCodes.Schedule(deps);

            deps.Complete();

            var constructBVHChild = new ConstructBVHChildNodes()
            {
                AABB = AABB,
                BVHArray = BVHArray,
                indexConverter = indexConverter
            };
            deps = constructBVHChild.Schedule(AABB.Length, 32, deps);

            var constructBVHInternal = new ConstructBVHInternalNodes()
            {
                BVHArray = BVHArray,
                mortonCodes = mortonCodes,
                NumObjects = AABB.Length
            };
            deps = constructBVHInternal.Schedule( AABB.Length - 1, 32, deps);

            NativeArray<int> locks = new NativeArray<int>(AABB.Length - 1, Allocator.TempJob);
            var updateAABB = new UpdateAABB()
            {
                BVHArray = BVHArray,
                locks = locks
            };

            /*for (int i = 0; i < AABB.Length; i++)
            {
                updateAABB.Execute(i);
            }*/

            deps = updateAABB.Schedule(AABB.Length, 32, deps);

            deps.Complete();
            for (int i = 0; i < BVHArray.Length - 1; i++)
            {
                if (BVHArray[i].IsValid > 0)
                {
                    DebugUtils.DrawAABB(BVHArray[i].aabb, UnityEngine.Random.ColorHSV());
               }
            }
            return deps;
        }

        public void Dispose()
        {
            mortonCodes.Dispose();
            mortonCodesTemp.Dispose();
            indexConverterTemp.Dispose();
            indexConverter.Dispose();
            radixSortBitValues.Dispose();
            radixSortOffsets.Dispose();
            sortResultsArrayIsA.Dispose();
            BVHArray.Dispose();
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
                bvhNode.IsValid = 1;
                BVHArray[bvhIndex] = bvhNode;
            }
        }


        //https://devblogs.nvidia.com/thinking-parallel-part-iii-tree-construction-gpu/
        [BurstCompile]
        struct ConstructBVHInternalNodes : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> mortonCodes;

            [NativeDisableParallelForRestriction]
            public NativeArray<BVHNode> BVHArray;

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

                int firstCode = mortonCodes[first];
                int lastCode = mortonCodes[last];

                if (firstCode == lastCode)
                    return (first + last) >> 1;

                // Calculate the number of highest bits that are the same
                // for all objects, using the count-leading-zeros intrinsic.

                int commonPrefix = clz(first, last);

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
                        int splitPrefix = clz(first, newSplit);
                        if (splitPrefix > commonPrefix)
                            split = newSplit; // accept proposal
                    }
                }
                while (step > 1);

                return split;
            }
            
            private int clz_safe(int idx, int idy)
            {
                math.countbits(0);
                if (idy < 0 || idy > NumObjects - 1) return -1;
                return clz(idx, idy);
            }

            //https://stackoverflow.com/questions/10439242/count-leading-zeroes-in-an-int32
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int clz(int idx, int idy)
            {
                int value = mortonCodes[idx] ^ mortonCodes[idy];
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
                int halfBVHSize = BVHArray.Length / 2;
                int2 range = determineRange(index);
                int first = range.x;
                int last = range.y;

                // Determine where to split the range.

                int split = findSplit(first, last);

                int childAIndex = split == first ? split + halfBVHSize : split;
                int childBIndex = (split + 1) == last ? split + halfBVHSize + 1 : split + 1;

                BVHNode thisnode = BVHArray[index];
                thisnode.RightNodeIndex = childBIndex;
                thisnode.LeftNodeIndex = childAIndex;
                thisnode.EntityId = -1;
                thisnode.IsValid = 0;//No AABB Updated
                BVHArray[index] = thisnode;

                BVHNode leftnode = BVHArray[childAIndex];
                leftnode.ParentNodeIndex = index;
                BVHArray[childAIndex] = leftnode;

                BVHNode rightnode = BVHArray[childBIndex];
                rightnode.ParentNodeIndex = index;
                BVHArray[childBIndex] = rightnode;
            }
        }

        [BurstCompile]
        struct UpdateAABB : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<BVHNode> BVHArray;
            
            [DeallocateOnJobCompletion]
            [NativeDisableParallelForRestriction]
            public NativeArray<int> locks;

            public void Execute(int i)
            {
                int halfLength = BVHArray.Length / 2;
                int leafNodeId = halfLength + i;
                AABB leafNodeAABB = BVHArray[leafNodeId].aabb;
                int parentIndex = BVHArray[leafNodeId].ParentNodeIndex;
                while (parentIndex != -1)
                {
                    //prevent data race, hold atomic value
                    while (locks[parentIndex] == 1) { }

                    locks[parentIndex] = 1;
                    BVHNode parent = BVHArray[parentIndex];
                    if (parent.IsValid == 0)
                    {
                        parent.aabb = leafNodeAABB;
                        parent.IsValid = 1;
                        BVHArray[parentIndex] = parent;
                        locks[parentIndex] = 0;
                        break;
                    }
                    else
                    {
                        parent.aabb = Utils.GetEncompassingAABB(parent.aabb, leafNodeAABB);
                    }
                    BVHArray[parentIndex] = parent;
                    locks[parentIndex] = 0;
                    leafNodeAABB = parent.aabb;
                    parentIndex = parent.ParentNodeIndex;
                }

            }
        }
    }
}
