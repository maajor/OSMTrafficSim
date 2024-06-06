using System;
using System.Collections.Generic;
using OSMTrafficSim.BVH;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
//using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace OSMTrafficSim
{
    [CreateAfter(typeof(PedestrianSystem))]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InstanceRenderingSystem : SystemBase
    {
        public Camera ActiveCamera;
        private NativeParallelMultiHashMap<int, Entity> _batcher;
        private NativeArray<float> _cullDistance;
        private List<InstanceRendererData> _renderData;
        private InstanceRenderer _renderer;
        private EntityQuery _queryGroup;

        protected override void OnCreate()
        {
            _renderData = new List<InstanceRendererData>();
            EntityManager.GetAllUniqueSharedComponentsManaged<InstanceRendererData>(_renderData);
            List<float> cullDistance = _renderData.ConvertAll( r => r.CullDistance);
            _cullDistance = new NativeArray<float>(cullDistance.ToArray(), Allocator.Persistent);
            _cullDistance.CopyFrom(cullDistance.ToArray());

            _batcher = new NativeParallelMultiHashMap<int, Entity>(10000, Allocator.Persistent);

            _renderer = new InstanceRenderer(EntityManager);
            
            _queryGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<InstanceRendererData>(),
                    ComponentType.ReadWrite<InstanceRendererProperty>(),
                    ComponentType.ReadOnly<LocalToWorld>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        protected override void OnDestroy()
        {
            _cullDistance.Dispose();
            _batcher.Dispose();
            _renderer.Dispose();
        }

        protected override void OnUpdate()
        {
        }

        public void Tick()
        {
            //if (ActiveCamera == null || !_batcher.IsCreated) return;
            //var dep = Dependency;
            ////share component id can only be visited by architypechunks, 
            ////so we iterate over architypechunks here
            ////https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/8f94d72d1fd9b8db896646d9d533055917dc265a/Documentation/reference/chunk_iteration.md
            //_batcher.Clear();
            //UnityEngine.Profiling.Profiler.BeginSample("gather chunks");
            //NativeArray<ArchetypeChunk> chunks = _queryGroup.ToArchetypeChunkArray(Allocator.TempJob);
            //UnityEngine.Profiling.Profiler.EndSample();
            //UnityEngine.Profiling.Profiler.BeginSample("start cull");
            //var cullJob = new CullJob()
            //{
            //    EntityType = GetEntityTypeHandle(),
            //    Chunks = chunks,
            //    RenderTypes = GetSharedComponentTypeHandle<InstanceRendererData>(),
            //    LocalToWorldType = GetComponentTypeHandle<LocalToWorld>(),
            //    Batcher = _batcher.AsParallelWriter(),
            //    CamPos = ActiveCamera.transform.position,
            //    CullDistance = _cullDistance
            //};
            //dep = cullJob.Schedule(chunks.Length, 1, dep);
            //dep.Complete();
            //if (_batcher.Count() > 0)
            //{
            //    Debug.Log("Has batch");
            //}
            //UnityEngine.Profiling.Profiler.EndSample();
            //UnityEngine.Profiling.Profiler.BeginSample("start render");
            //Render();
            //UnityEngine.Profiling.Profiler.EndSample();
            //Dependency = dep;
        }

        public void Render()
        {
            //_renderer.Clean(ActiveCamera);
            for (int i = 0; i < _renderData.Count; i++)
            {
                if (_renderData[i].Material && _renderData[i].Mesh)
                {
                    Entity ent;
                    if (_batcher.TryGetFirstValue(i, out ent, out var iterator))
                    {
                        InstanceRendererProperty prop = EntityManager.GetComponentData<InstanceRendererProperty>(ent);
                        _renderer.Init(_renderData[i], prop);
                        _renderer.Batch(ent);
                        while (_batcher.TryGetNextValue(out ent, ref iterator))
                        {
                            _renderer.Batch(ent);
                        }
                        _renderer.Final();
                    }
                }
            }
        }
        

    }
}
