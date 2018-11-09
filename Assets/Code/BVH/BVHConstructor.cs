using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OSMTrafficSim.BVH
{
    public unsafe class BVHConstructor
    {

        public BVHConstructor(int capacity)
        {
            _capacity = capacity;
            int mortonCodeLength = Utils.GetNextHighestPowerOf2(Utils.ceil_pow2(_capacity));
            mortonCodes = new NativeArray<uint>(mortonCodeLength, Allocator.Persistent);
            indexConverter = new NativeArray<int>(mortonCodeLength, Allocator.Persistent);
            bounds = new NativeArray<AABB>(8, Allocator.Persistent);

            int requiredBVHLength = (Utils.GetNextHighestPowerOf2(Utils.ceil_pow2(_capacity) + 1)) - 1;

            //locks = (long*)UnsafeUtility.Malloc(
            //    requiredBVHLength * sizeof(int), sizeof(int), Allocator.Persistent);
            BVHArray = new NativeArray<BVHNode>(requiredBVHLength, Allocator.Persistent);
            parentIndex = new NativeArray<int>(requiredBVHLength, Allocator.Persistent);
            bDebug = false;
        }

        public NativeArray<BVHNode> BVHArray;

        public bool bDebug;
        //private long* locks;
        private int _capacity;
        private NativeArray<uint> mortonCodes;
        private NativeArray<int> indexConverter;
        private NativeArray<int> parentIndex;
        private NativeArray<AABB> bounds;

        public JobHandle Calculate(JobHandle deps, ComponentDataArray<AABB> AABB)
        {
            var computeBound = new CalculateBoundsJob()
            {
                AABB = AABB,
                batchSize = (int) math.ceil(AABB.Length / 8),
                results = bounds
            };
            deps = computeBound.Schedule(8, 1, deps);

            var boundMerge = new CalculateBoundsMergedJob() {results = bounds};
            deps = boundMerge.Schedule(deps);
            
            var resetBVH = new ResetBVH()
            {
                BVHArray = BVHArray,
            };
            deps = resetBVH.Schedule(BVHArray.Length, 32, deps);

            var computeMortonJob = new ComputeMortonCodesJob
            {
                aabbs = AABB,
                indexConverter = indexConverter,
                mortonCodes = mortonCodes,
                Bounds = bounds
            };
            deps = computeMortonJob.Schedule(mortonCodes.Length, 64, deps);
            
            var bitonicMergeJob = new BitonicMergeJob()
            {
                values = mortonCodes,
                indexConverter = indexConverter
            };
            var bitonicSortJob = new BitonicSortJob()
            {
                indexConverter = indexConverter,
                values = mortonCodes
            };
            int pass = (int)math.log2(mortonCodes.Length);
            for (int i = 0; i < pass - 1; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    bitonicMergeJob.strideSwap = 1 << (i - j);
                    bitonicMergeJob.strideRisingGroup = 1 << j;
                    deps = bitonicMergeJob.Schedule(mortonCodes.Length / 2, 64, deps);

                }
            }
            for (int i = 0; i < pass; i++)
            {
                bitonicSortJob.strideSwap = 1 << (pass - i - 1);
                deps = bitonicSortJob.Schedule(mortonCodes.Length / 2, 64, deps);
            }
            
            var constructBVHChild = new ConstructBVHChildNodesJob()
            {
                AABB = AABB,
                BVHArray = BVHArray,
                indexConverter = indexConverter
            };
            deps = constructBVHChild.Schedule(BVHArray.Length, 32, deps);
            
            var constructBVHInternal = new ConstructBVHInternalNodesJob()
            {
                BVHArray = BVHArray,
                mortonCodes = mortonCodes,
                NumObjects = AABB.Length,
                ParentIndex = parentIndex
            };
            deps = constructBVHInternal.Schedule( AABB.Length - 1, 32, deps);

            var updateParentIndex = new UpdateBVHParentIndexJob()
            {
                BVHArray = BVHArray,
                ParentIndex = parentIndex
            };
            deps = updateParentIndex.Schedule(BVHArray.Length, 32, deps);

            var updateAABB = new UpdateAABBJob()
            {
                BVHArray = BVHArray
            };
            deps = updateAABB.Schedule(AABB.Length, 32, deps);

            if (bDebug)
            {
                deps.Complete();
                Debug.Assert(DebugUtils.ValidateBVH(BVHArray));
                for (int i = 0; i < BVHArray.Length - 1; i++)
                {
                    if (BVHArray[i].IsValid > 0)
                    {
                        DebugUtils.DrawAABB(BVHArray[i].aabb, UnityEngine.Random.ColorHSV());
                    }
                }
            }
            return deps;
        }

        public void Dispose()
        {
            mortonCodes.Dispose();
            indexConverter.Dispose();
            BVHArray.Dispose();
            parentIndex.Dispose();
            bounds.Dispose();
        }
    }
}
