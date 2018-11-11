using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;
using OSMTrafficSim.BVH;
#if UNITY_EDITOR
using UnityEditor;
#endif
using WalkablePatch = System.UInt64;

namespace OSMTrafficSim
{

    public class PedestrianArea : MonoBehaviour
    {
        private static PedestrianArea _instance;
        public static PedestrianArea Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GameObject.FindObjectOfType<PedestrianArea>();
                }
                return _instance;
            }
        }

        public bool bDebug;
        public Vector3 Size;
        public List<WalkablePatch> WalkableArea;//one ulong for 8 * 8 patch.
        public int PatchResolution = 256;
        [HideInInspector]
        public Random RandomGen;

        public Vector3 startPos;

        public void Awake()
        {
            _instance = this;
        }

        public void InitRandom()
        {
            RandomGen = new Unity.Mathematics.Random();
            // RandomGen.InitState((uint)System.DateTime.Now.Millisecond);
            RandomGen.InitState();
        }

        public void GetNextRandomPosition(out float3 pos, out quaternion rot, out float3 forward, out float2 localPos, out float speed, out int2 gridId)
        {
            int find = 0;
            pos= new float3();
            rot = new quaternion();
            forward = new float3();
            gridId = new int2();
            speed = 0;
            localPos = new float2();
            float3 min = startPos - Size / 2;
            float3 max = startPos + Size / 2;
            while (find == 0)
            {
                pos = RandomGen.NextFloat3(min, max);
                gridId = GetGridId(pos);
                if (!IsWalkable(gridId)) continue;
                pos.y = 0;
                forward = RandomGen.NextFloat3Direction();
                forward.y = 0;
                forward = math.normalize(forward);
                rot = quaternion.LookRotation(forward, new float3(0,1,0));
                localPos = GetLocalPos(gridId, pos);
                speed = RandomGen.NextFloat(1.0f, 2.0f);
                find = 1;
            }

            return;
        }

        private int2 GetGridId(float3 position)
        {
            float div_x = Size.x / (PatchResolution * 8);
            float div_z = Size.z / (PatchResolution * 8);
            Vector3 min = startPos - Size / 2;
            int x = (int)((position.x - min.x) / div_x);
            int z = (int)((position.z - min.z) / div_z);
            return new int2(x,z);
        }

        private float2 GetLocalPos(int2 gridid, float3 worldPos)
        {
            float2 texelSize = new float2(Size.x / (PatchResolution * 8), Size.z / (PatchResolution * 8));
            float2 cornerStart = texelSize * gridid + new float2(startPos.x, startPos.z) - new float2(Size.x / 2, Size.z / 2);
            return (worldPos.xz - cornerStart) / texelSize;
        }

        private bool IsWalkable(int2 gridId)
        {
            if (gridId.x < 0 || gridId.y < 0 || gridId.x >= PatchResolution * 8 || gridId.y >= PatchResolution * 8) return false;
            int2 patchId = gridId / 8;
            int2 patchLocalId = gridId - patchId * 8;
            return 1ul == ((WalkableArea[patchId.x * PatchResolution + patchId.y] >> (patchLocalId.x * 8 + patchLocalId.y)) & 1ul);
        }

#if UNITY_EDITOR

        public void BuildWalkableArea()
        {
            WalkableArea[] areas = gameObject.GetComponentsInChildren<WalkableArea>();
            if (areas.Length == 0) return;
            Bounds bound = new Bounds();
            bool empty = true;
            foreach (var area in areas)
            {
                area.OnBuild();
                if (empty){
                    bound = area.Col.bounds;
                    empty = false;
                }
                bound.Encapsulate(area.Col.bounds);
            }

            startPos = bound.center;
            Size = bound.size;

            WalkableArea.Clear();
            float div_x = Size.x / PatchResolution;
            float div_z = Size.z / PatchResolution;
            Vector3 min = startPos - Size / 2;
            for (int i = 0; i < PatchResolution; i++)
            {
                for (int j = 0; j < PatchResolution; j++)
                {
                    int progress = i * PatchResolution + j;
                    EditorUtility.DisplayProgressBar("Build!", "Progressing", (float)progress / (PatchResolution * PatchResolution) );
                    Vector3 patch_min = min + new Vector3(i * div_x, 0, j * div_z);
                    Vector3 patch_max = min + new Vector3((i + 1) * div_x, 0, (j + 1) * div_z);
                    WalkableArea.Add(BuildPatch(patch_min, patch_max));
                }
            }
            foreach (var area in areas)
            {
                area.OnFinish();
            }
            EditorUtility.ClearProgressBar();
        }

        private WalkablePatch BuildPatch(Vector3 min, Vector3 max)
        {
            WalkablePatch result = 0;
            float div_x = (max.x - min.x) / 8;
            float div_z = (max.z - min.z) / 8;
            bool[] conserveGrid = new bool[9 * 9];
            //conservative
            for (int i = 0; i <= 8; i++)
            {
                for (int j = 0; j <= 8; j++)
                {
                    Vector3 texelCenter = min + new Vector3((i) * div_x, 0, (j) * div_z);
                    Ray castRay = new Ray(new Vector3(texelCenter.x, 1000, texelCenter.z), new Vector3(0, -1, 0));
                    int id = i * 9 + j;
                    RaycastHit[] castHitObstacles = Physics.RaycastAll(castRay, 2000.0f, 1 << 11 | 1 << 12);
                    RaycastHit[] castHitAreas = Physics.RaycastAll(castRay, 2000.0f, 1 << 31);
                    if (castHitObstacles.Length == 0 && castHitAreas.Length > 0)
                    {
                        conserveGrid[id] = true;
                    }
                }
            }

            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    int bitPos = i * 8 + j;
                    int id1 = i * 9 + j;
                    int id2 = i * 9 + j + 1;
                    int id3 = (i + 1) * 9 + j;
                    int id4 = (i + 1) * 9 + j + 1;
                    bool hit = conserveGrid[id1] && conserveGrid[id2] && conserveGrid[id3] && conserveGrid[id4];
                    if (hit) result |= 1ul << bitPos;
                }
            }

            return result;
        }

        void OnDrawGizmos()
        {
            if (bDebug)
            {
                Color boxcolor = new Color(0.5f, 0, 0, 0.2f);
                Gizmos.color = boxcolor;
                Gizmos.DrawCube(startPos, Size);
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(startPos, Size);
                DebugDrawWalkableArea();
            }
        }

        void DebugDrawWalkableArea()
        {
            if (WalkableArea.Count != PatchResolution * PatchResolution) return;
            float div_x = Size.x / PatchResolution;
            float div_z = Size.z / PatchResolution;
            Vector3 min = startPos - Size / 2;
            for (int i = 0; i < PatchResolution; i++)
            {
                for (int j = 0; j < PatchResolution; j++)
                {
                    Vector3 patch_min = min + new Vector3(i * div_x, 0, j * div_z);
                    Vector3 patch_max = min + new Vector3((i + 1) * div_x, 0, (j + 1) * div_z);
                    int itemid = i * PatchResolution + j;
                    DebugDrawWalkablePatch(WalkableArea[itemid], patch_min, patch_max);
                }
            }
        }

        void DebugDrawWalkablePatch(WalkablePatch patch, Vector3 min, Vector3 max)
        {
            float div_x = (max.x - min.x) / 8;
            float div_z = (max.z - min.z) / 8;
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    Vector3 texelCenter = min + new Vector3((i + 0.5f) * div_x, 0, (j + 0.5f) * div_z);
                    texelCenter.y = 0;
                    int bitPos = i * 8 + j;
                    int Walkable = (int) (patch >> bitPos & 1ul);
                    if (Walkable == 1)
                    {
                        Gizmos.DrawWireCube(texelCenter, new Vector3(0.5f, 0.5f, 0.5f));
                    }
                }
            }
        }
#endif
    }
}
