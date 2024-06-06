using System.Collections;
using System.Collections.Generic;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace OSMTrafficSim
{
    [BurstCompile]
    struct CullJob : IJobParallelFor
    {
        [ReadOnly]
        public EntityTypeHandle EntityType;
        [DeallocateOnJobCompletion]
        [ReadOnly]
        public NativeArray<ArchetypeChunk> Chunks;
        [ReadOnly]
        public SharedComponentTypeHandle<InstanceRendererData> RenderTypes;
        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> LocalToWorldType;
        [WriteOnly]public NativeList<Entity>.ParallelWriter Batcher;
        [ReadOnly]
        public float CullDistance;

        public float3 CamPos;

        public void Execute(int index)
        {
            int renderIndex = Chunks[index].GetSharedComponentIndex(RenderTypes);
            var entitiesSlice = Chunks[index].GetNativeArray(EntityType);
            var localToWorldsSlice = Chunks[index].GetNativeArray(ref LocalToWorldType);
            for (int i = 0; i < localToWorldsSlice.Length; i++)
            {
                if (math.distance(localToWorldsSlice[i].Value.c3.xyz, CamPos) < CullDistance)
                {
                    Batcher.AddNoResize(entitiesSlice[i]);
                }
            }
        }
    }
}
