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
using UnityEngine.Assertions;

namespace OSMTrafficSim
{
    public unsafe class BVHConstructor
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

            //locks = (long*)UnsafeUtility.Malloc(
            //    requiredBVHLength * sizeof(int), sizeof(int), Allocator.Persistent);
            BVHArray = new NativeArray<BVHNode>(requiredBVHLength, Allocator.Persistent);
            parentIndex = new NativeArray<int>(requiredBVHLength, Allocator.Persistent);
            bDebug = true;
        }

        public NativeArray<BVHNode> BVHArray;

        public bool bDebug;
        //private long* locks;
        private int _capacity;
        private NativeArray<int> mortonCodes;
        private NativeArray<int> mortonCodesTemp;
        private NativeArray<int> indexConverterTemp;
        private NativeArray<int> indexConverter;
        private NativeArray<int> radixSortBitValues;
        private NativeArray<int> radixSortOffsets;
        private NativeArray<int> sortResultsArrayIsA;
        private NativeArray<int> parentIndex;

        private AABB GetBoundingBox(ComponentDataArray<AABB> AABB)
        {
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
            //UnityEngine.Profiling.Profiler.BeginSample("update bbx");
            var bound = GetBoundingBox(AABB);
            //UnityEngine.Profiling.Profiler.EndSample();
            //UnityEngine.Profiling.Profiler.BeginSample("update bvh");
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

            deps.Complete();
            var constructBVHInternal = new ConstructBVHInternalNodes()
            {
                BVHArray = BVHArray,
                mortonCodes = mortonCodes,
                NumObjects = AABB.Length,
                ParentIndex = parentIndex
            };
            deps = constructBVHInternal.Schedule( AABB.Length - 1, 32, deps);

            var updateParentIndex = new UpdateBVHParentIndex()
            {
                BVHArray = BVHArray,
                ParentIndex = parentIndex
            };
            deps = updateParentIndex.Schedule(BVHArray.Length, 32, deps);
            deps.Complete();
            var updateAABB = new UpdateAABB()
            {
                BVHArray = BVHArray,
                //locks = locks
            };
            for (int i = 0; i < AABB.Length; i++)
            {
                //updateAABB.Execute(i);
            }

            deps = updateAABB.Schedule(AABB.Length, 32, deps);
            //UnityEngine.Profiling.Profiler.EndSample();
            if (bDebug)
            {
                deps.Complete();
                //Debug.Assert(ValidateBVH(BVHArray));
            }
            /*for (int i = 0; i < BVHArray.Length - 1; i++)
            {
                if (BVHArray[i].IsValid > 0)
                {
                    //DebugUtils.DrawAABB(BVHArray[i].aabb, UnityEngine.Random.ColorHSV());
               }
                if (BVHArray[i].IsValid  == 1)
                {
                   // Debug.Log("error at " + i);
                }
            }*/
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
            parentIndex.Dispose();
        }

        public bool ValidateBVH(NativeArray<BVHNode> BVHArray)
        {
            int[] used = new int[BVHArray.Length];
            used[0] = 1;
            Stack<int> nextnodes = new Stack<int>();
            nextnodes.Push(0);
            while (nextnodes.Count > 0)
            {
                int val = nextnodes.Pop();
                BVHNode thisnode = BVHArray[val];
                if (thisnode.LeftNodeIndex >= 0 && val != thisnode.LeftNodeIndex)
                {
                    nextnodes.Push(thisnode.LeftNodeIndex);
                    used[thisnode.LeftNodeIndex]++;
                }

                if (thisnode.RightNodeIndex >= 0 && val != thisnode.RightNodeIndex)
                {
                    nextnodes.Push(thisnode.RightNodeIndex);
                    used[thisnode.RightNodeIndex]++;
                }
            }

            bool result = true;
            for (int i = 0; i < BVHArray.Length; i++)
            {
                if (used[i] > 1)
                {
                    Debug.Log("multiple " + i);
                    result = false;
                }
            }

            //DrawTree(BVHArray, 0, 0);
            return result;
        }

        public void DrawTree(NativeArray<BVHNode> BVHArray, int id, int depth)
        {
            float maxdepth = math.log2(BVHArray.Length);
            int halfl = BVHArray.Length  / 2;
            int left = BVHArray[id].LeftNodeIndex;
            int right = BVHArray[id].RightNodeIndex;
            int leftnorm = left;
            int rightnorm = right;
            Vector3 rootP = new Vector3(-id, 0, depth * 30);
            Vector3 leftP = new Vector3(-leftnorm, 0, (depth + 1) * 30 );
            Vector3 rightP = new Vector3(-rightnorm, 0, (depth + 1) * 30 );
            if (left >= halfl)
            {
                leftnorm = left - halfl;
                leftP = new Vector3(-leftnorm, 0, maxdepth * 30 * 1.6f);
            }
            if (right >= halfl)
            {
                rightnorm = right - halfl;
                rightP = new Vector3(-rightnorm, 0, maxdepth * 30 * 1.6f);
            }


            if (left != -1)
            {
                Color col = BVHArray[left].IsValid == 2 ? Color.white : Color.red;
                Debug.DrawLine(rootP, leftP, col);
            }

            if (right != id)
            {
                Color col = BVHArray[right].IsValid == 2 ? Color.white : Color.red;
                Debug.DrawLine(rootP,rightP, col);
            }

            if (left != -1 && left != id)
            {
                DrawTree(BVHArray, left, depth + 1);
            }
            if (right != -1 && right != id)
            {
                DrawTree(BVHArray, right, depth + 1);
            }
        }
    }
}
