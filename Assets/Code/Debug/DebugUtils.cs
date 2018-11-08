using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEditor;
using UnityEngine.Assertions;
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

        [MenuItem("OSMTrafficSim/DebugCLZ")]
        private static void DebugClz()
        {
            Random rd = new Random((uint)DateTime.Now.Millisecond);
            for (int i = 0; i < 1000; i++)
            {
                int clz_true = rd.NextInt(30);
                int bits = 31 - clz_true;
                int add1 = rd.NextInt(0, (1 << (bits - 1)) - 1);
                int add2 = rd.NextInt(0, (1 << (bits - 1)) - 1);
                int rand1 = add1 + (1 << bits);
                int rand2 = add2 + (1 << (bits - 1));
                int clz_calc = clz(rand1, rand2);
                Debug.Assert(clz_calc == clz_true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int clz(int x, int y)
        {
            int value = x ^ y;
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
