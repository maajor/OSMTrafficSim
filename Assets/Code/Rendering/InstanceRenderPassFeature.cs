using OSMTrafficSim.BVH;
using OSMTrafficSim;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections;
using System.Linq;
using Unity.Jobs;
using System.Collections.Generic;
using Unity.Android.Gradle.Manifest;
using UnityEngine.UIElements;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86.Avx;

public class InstanceRenderPassFeature : ScriptableRendererFeature
{
    class InstanceRenderPass : ScriptableRenderPass
    {
        private EntityQuery _queryGroup;
        private NativeList<Entity> _batcher;
        private EntityManager _entityManager;
        private List<InstanceRendererData> _renderData;
        private CommandBuffer _commandBuffer;

        private Vector4[] param;
        private Matrix4x4[] transforms;
        private NativeArray<float4x4> matrices;
        private NativeArray<float4> propertyParams;
        private MaterialPropertyBlock propertyBlock;

        private int batchCount;
        private int shaderId;
        private InstanceRendererData data;
        private bool inited = false;
        private const int batchSize = 1023;

        public InstanceRenderPass()
        {
            inited = false;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _entityManager = world.EntityManager;
            if (_entityManager == null) return;
            _queryGroup = _entityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<InstanceRendererData>(),
                    ComponentType.ReadWrite<InstanceRendererProperty>(),
                    ComponentType.ReadOnly<LocalToWorld>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
            _renderData = new List<InstanceRendererData>();
            _entityManager.GetAllUniqueSharedComponentsManaged<InstanceRendererData>(_renderData);
            _batcher = new NativeList<Entity>(10000, Allocator.Persistent);
            param = new Vector4[batchSize];
            transforms = new Matrix4x4[batchSize];

            matrices = new NativeArray<float4x4>(batchSize, Allocator.Persistent);
            propertyParams = new NativeArray<float4>(batchSize, Allocator.Persistent);
            propertyBlock = new MaterialPropertyBlock();
            inited = true;
        }

        public void Dispose()
        {
            matrices.Dispose();
            propertyParams.Dispose();
            _batcher.Dispose();
        }

        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _commandBuffer = cmd;
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!inited || _commandBuffer == null) return;


            _batcher.Clear();
            UnityEngine.Profiling.Profiler.BeginSample("gather chunks");
            NativeArray<ArchetypeChunk> chunks = _queryGroup.ToArchetypeChunkArray(Allocator.TempJob);
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("start cull");
            var cullJob = new CullJob()
            {
                EntityType = _entityManager.GetEntityTypeHandle(),
                Chunks = chunks,
                RenderTypes = _entityManager.GetSharedComponentTypeHandle<InstanceRendererData>(),
                LocalToWorldType = _entityManager.GetComponentTypeHandle<LocalToWorld>(true),
                Batcher = _batcher.AsParallelWriter(),
                CamPos = renderingData.cameraData.worldSpaceCameraPos,
                CullDistance = 500
            };
            var deps = cullJob.Schedule(chunks.Length, 1);
            deps.Complete();
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("start render");
            Render();
            UnityEngine.Profiling.Profiler.EndSample();
            context.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            _commandBuffer = cmd;
        }

        public void Render()
        {
            for (int i = 0; i < _renderData.Count; i++)
            {
                if (_renderData[i].Material && _renderData[i].Mesh)
                {
                    data = _renderData[i];
                    foreach (var ent in _batcher)
                    {
                        Batch(ent);
                    }
                    Submit();
                }
            }
        }

        private void Submit()
        {
            if (batchCount == 0) return; 
            Utils.CopyToFloat4(propertyParams, param);
            Utils.CopyToFloat4x4(matrices, transforms);
            propertyBlock.SetVectorArray(shaderId, param);
            _commandBuffer.DrawMeshInstanced(data.Mesh, data.SubMesh, data.Material, 0, transforms, batchCount, propertyBlock);
            batchCount = 0;
        }

        private void Batch(Entity ent)
        {
            if (batchCount >= batchSize)
            {
                Submit();
            }
            var loc = _entityManager.GetComponentData<LocalToWorld>(ent);
            var prop = _entityManager.GetComponentData<InstanceRendererProperty>(ent);
            matrices[batchCount] = loc.Value;
            propertyParams[batchCount] = prop.Value;
            batchCount++;
        }

    }

    InstanceRenderPass mPass;

    /// <inheritdoc/>
    public override void Create()
    {
        mPass = new InstanceRenderPass();

        // Configures where the render pass should be injected.
        mPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(mPass);
    }

    protected override void Dispose(bool disposing)
    {
        if(mPass != null) mPass.Dispose();
    }
}


