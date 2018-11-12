using System;
using System.Collections.Generic;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{
    public class InstanceRenderingSystem : ComponentSystem
    {
        struct RenderGroup
        {
            public readonly int Length;
            public EntityArray Entity;
            [ReadOnly]
            public SharedComponentDataArray<InstanceRendererData> Renderer;
            public ComponentDataArray<InstanceRendererProperty> Property;
            public ComponentDataArray<LocalToWorld> LocalToWorld;
        }
        [Inject] RenderGroup _renderGroup;

        public Camera ActiveCamera;
        public NativeMultiHashMap<int, Entity> Batcher;
        //private ArchetypeChunkArray chunks;
        private EntityArchetypeQuery _query;
        public NativeArray<float> CullDistance;
        private List<InstanceRendererData> renders;

        protected override void OnCreateManager()
        {
            _query = new EntityArchetypeQuery()
            {
                Any = Array.Empty<ComponentType>(),
                None = Array.Empty<ComponentType>(),
                All = new ComponentType[] { typeof(InstanceRendererData), typeof(InstanceRendererProperty), typeof(LocalToWorld) }
            };
            renders = new List<InstanceRendererData>();
            EntityManager.GetAllUniqueSharedComponentData(renders);
            List<float> cullDistance = new List<float>(renders.Count);
            for (int j = 0; j < renders.Count; j++)
            {
                cullDistance.Add(renders[j].CullDistance);
            }
            CullDistance = new NativeArray<float>(cullDistance.ToArray(), Allocator.Persistent);
            CullDistance.CopyFrom(cullDistance.ToArray());

            Batcher = new NativeMultiHashMap<int, Entity>(1000000, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            CullDistance.Dispose();
            Batcher.Dispose();
        }

        protected override void OnUpdate()
        {
            if (ActiveCamera == null) return;
            Batcher.Clear();
            NativeArray<ArchetypeChunk> chunks = EntityManager.CreateArchetypeChunkArray(_query, Allocator.TempJob);
            var instanceRendererType = GetArchetypeChunkSharedComponentType<InstanceRendererData>();
            var cullJob = new CullJob()
            {
                EntityType = GetArchetypeChunkEntityType(),
                Chunks = chunks,
                RenderTypes = instanceRendererType,
                LocalToWorldType = GetArchetypeChunkComponentType<LocalToWorld>(true),
                Batcher = Batcher.ToConcurrent(),
                CamPos = ActiveCamera.transform.position,
                CullDistance = CullDistance
            };

            for (int i = 0; i < chunks.Length; i++)
            {
               // cullJob.Execute(i);
            }

            //Debug.Log(Batcher.Length);

            var deps = cullJob.Schedule(chunks.Length, 32);
            deps.Complete();
            
            Vector4[] param = new Vector4[1023];
            Matrix4x4[] transforms = new Matrix4x4[1023];
            for (int i = 0; i < renders.Count; i++)
            {
                if (renders[i].Material && renders[i].Mesh)
                {
                    Entity ent;
                    NativeMultiHashMapIterator<int> iterator;
                    int batchCount = 0;
                    NativeArray<float4x4> matrices = new NativeArray<float4x4>(1023, Allocator.Temp);
                    NativeArray<float4> propertyParams = new NativeArray<float4>(1023, Allocator.Temp);
                    MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                    int propid = 0;
                    if (Batcher.TryGetFirstValue(i, out ent, out iterator))
                    {
                        LocalToWorld loc = EntityManager.GetComponentData<LocalToWorld>(ent);
                        InstanceRendererProperty prop = EntityManager.GetComponentData<InstanceRendererProperty>(ent);
                        matrices[batchCount] = loc.Value;
                        propertyParams[batchCount] = prop.Value;
                        propid = prop.ParamId;
                        batchCount++;
                        while (Batcher.TryGetNextValue(out ent, ref iterator))
                        {
                            if (batchCount >= 1023)
                            {
                                CopyToFloat4(propertyParams, param);
                                CopyToFloat4x4(matrices, transforms);
                                propertyBlock.SetVectorArray(propid, param);
                                Graphics.DrawMeshInstanced(renders[i].Mesh, renders[i].SubMesh, renders[i].Material, transforms, batchCount, propertyBlock, renders[i].CastShadows, false);
                                batchCount = 0;
                            }
                            loc = EntityManager.GetComponentData<LocalToWorld>(ent);
                            prop = EntityManager.GetComponentData<InstanceRendererProperty>(ent);
                            matrices[batchCount] = loc.Value;
                            propertyParams[batchCount] = prop.Value;
                            batchCount++;
                        }
                        CopyToFloat4(propertyParams, param);
                        CopyToFloat4x4(matrices, transforms);
                        propertyBlock.SetVectorArray(propid, param);
                        Graphics.DrawMeshInstanced(renders[i].Mesh, renders[i].SubMesh, renders[i].Material, transforms, batchCount, propertyBlock, renders[i].CastShadows, false);
                    }
                    matrices.Dispose();
                    propertyParams.Dispose();
                }
            }


        }

        class InstanceRenderer
        {
            NativeArray<float4x4> matrices = new NativeArray<float4x4>(1023, Allocator.Temp);
            NativeArray<float4> propertyParams = new NativeArray<float4>(1023, Allocator.Temp);
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

            public InstanceRenderer()
            {

            }

            public void Submit()
            {

            }

            public void Batch()
            {

            }

            public void Finalize()
            {

            }
        }

        static unsafe void CopyToFloat4x4(NativeArray<float4x4> transforms, Matrix4x4[] outMatrices)
        {
            fixed (Matrix4x4* resultMatrices = outMatrices)
            {
                float4x4* sourceMatrices = (float4x4*)transforms.GetUnsafeReadOnlyPtr();
                UnsafeUtility.MemCpy(resultMatrices, sourceMatrices, UnsafeUtility.SizeOf<Matrix4x4>() * transforms.Length);
            }
        }

        static unsafe void CopyToFloat4(NativeArray<float4> param, Vector4[] outVectors)
        {
            fixed (Vector4* resultMatrices = outVectors)
            {
                float4* sourceMatrices = (float4*)param.GetUnsafeReadOnlyPtr();
                UnsafeUtility.MemCpy(resultMatrices, sourceMatrices, UnsafeUtility.SizeOf<Vector4>() * param.Length);
            }
        }

        struct CullJob : IJobParallelFor
        {
            [ReadOnly]
            public ArchetypeChunkEntityType EntityType;
            [DeallocateOnJobCompletion]
            public NativeArray<ArchetypeChunk> Chunks;
            [ReadOnly]
            public ArchetypeChunkSharedComponentType<InstanceRendererData> RenderTypes;
            [ReadOnly]
            public ArchetypeChunkComponentType<LocalToWorld> LocalToWorldType;
            public NativeMultiHashMap<int, Entity>.Concurrent Batcher;
            public NativeArray<float> CullDistance;

            public float3 CamPos;

            public void Execute(int index)
            {
                int renderIndex = Chunks[index].GetSharedComponentIndex(RenderTypes);
                var entitiesSlice = Chunks[index].GetNativeArray(EntityType);
                var localToWorlsSlice = Chunks[index].GetNativeArray(LocalToWorldType);
                for (int i = 0; i < localToWorlsSlice.Length; i++)
                {
                    if (math.distance(localToWorlsSlice[i].Value.c3.xyz, CamPos) < CullDistance[renderIndex])
                    {
                        Batcher.Add(renderIndex, entitiesSlice[i]);
                    }
                }
            }
        }

    }
}
