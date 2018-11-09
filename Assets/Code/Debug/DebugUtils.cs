using OSMTrafficSim.BVH;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace OSMTrafficSim
{
    public class DebugUtils
    { 
        public static void DrawAABB(AABB aabb, Color color)
        {
            Debug.DrawLine(new Vector3(aabb.Min.x, aabb.Min.y, aabb.Min.z), new Vector3(aabb.Max.x, aabb.Min.y, aabb.Min.z), color);
            Debug.DrawLine(new Vector3(aabb.Max.x, aabb.Min.y, aabb.Min.z), new Vector3(aabb.Max.x, aabb.Min.y, aabb.Max.z), color);
            Debug.DrawLine(new Vector3(aabb.Max.x, aabb.Min.y, aabb.Max.z), new Vector3(aabb.Min.x, aabb.Min.y, aabb.Max.z), color);
            Debug.DrawLine(new Vector3(aabb.Min.x, aabb.Min.y, aabb.Max.z), new Vector3(aabb.Min.x, aabb.Min.y, aabb.Min.z), color);

            Debug.DrawLine(new Vector3(aabb.Min.x, aabb.Min.y, aabb.Min.z), new Vector3(aabb.Min.x, aabb.Max.y, aabb.Min.z), color);
            Debug.DrawLine(new Vector3(aabb.Max.x, aabb.Min.y, aabb.Min.z), new Vector3(aabb.Max.x, aabb.Max.y, aabb.Min.z), color);
            Debug.DrawLine(new Vector3(aabb.Max.x, aabb.Min.y, aabb.Max.z), new Vector3(aabb.Max.x, aabb.Max.y, aabb.Max.z), color);
            Debug.DrawLine(new Vector3(aabb.Min.x, aabb.Min.y, aabb.Max.z), new Vector3(aabb.Min.x, aabb.Max.y, aabb.Max.z), color);

            Debug.DrawLine(new Vector3(aabb.Min.x, aabb.Max.y, aabb.Min.z), new Vector3(aabb.Max.x, aabb.Max.y, aabb.Min.z), color);
            Debug.DrawLine(new Vector3(aabb.Max.x, aabb.Max.y, aabb.Min.z), new Vector3(aabb.Max.x, aabb.Max.y, aabb.Max.z), color);
            Debug.DrawLine(new Vector3(aabb.Max.x, aabb.Max.y, aabb.Max.z), new Vector3(aabb.Min.x, aabb.Max.y, aabb.Max.z), color);
            Debug.DrawLine(new Vector3(aabb.Min.x, aabb.Max.y, aabb.Max.z), new Vector3(aabb.Min.x, aabb.Max.y, aabb.Min.z), color);
        }

        public static void DebugConnection(ComponentDataArray<Position> pos, ComponentDataArray<HitResult> hitresult)
        {
            for (int i = 0; i < pos.Length; i++)
            {
                int frontid = hitresult[i].FrontEntityId;
                if (frontid != 0)
                {
                    Debug.DrawLine(pos[i].Value, pos[frontid].Value);
                }
            }
        }

        public static bool ValidateBVH(NativeArray<BVHNode> BVHArray)
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

            DrawAABB(BVHArray);

            DrawTree(BVHArray, 0, 0);
            return result;
        }

        public static void DrawAABB(NativeArray<BVHNode> BVHArray)
        {
            for (int i = 0; i < BVHArray.Length - 1; i++)
            {
                if (BVHArray[i].IsValid > 0)
                {
                    DebugUtils.DrawAABB(BVHArray[i].aabb, UnityEngine.Random.ColorHSV());
                }
            }
        }

        public static void DrawTree(NativeArray<BVHNode> BVHArray, int id, int depth)
        {
            float maxdepth = math.log2(BVHArray.Length);
            int halfl = BVHArray.Length / 2;
            int left = BVHArray[id].LeftNodeIndex;
            int right = BVHArray[id].RightNodeIndex;
            int leftnorm = left;
            int rightnorm = right;
            Vector3 rootP = new Vector3(-id, 0, depth * 30);
            Vector3 leftP = new Vector3(-leftnorm, 0, (depth + 1) * 30);
            Vector3 rightP = new Vector3(-rightnorm, 0, (depth + 1) * 30);
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
                Debug.DrawLine(rootP, rightP, col);
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

        [MenuItem("OSMTrafficSim/DebugCLZ")]
        private static void DebugClz()
        {
            Random rd = new Random((uint)DateTime.Now.Millisecond);
            for (int i = 0; i < 10000; i++)
            {
                uint clz_true = rd.NextUInt(30);
                uint bits = 31 - clz_true;
                uint add1 = rd.NextUInt(0, (1u << (int)(bits - 1u)) - 1u);
                uint add2 = rd.NextUInt(0, (1u << (int)(bits - 1u)) - 1u);
                uint rand1 = add1 + (1u << (int)bits);
                uint rand2 = add2 + (1u << (int)(bits - 1u));
                uint clz_calc = clz(rand1, rand2);
                Debug.Assert(clz_calc == clz_true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint clz(uint x, uint y)
        {
            uint value = x ^ y;
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
    }
}
