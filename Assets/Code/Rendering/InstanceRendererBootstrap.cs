using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
//using Unity.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InstanceRendererBootstrap : SystemBase
    {

        InstanceRenderingSystem instanceRendererSystem;

        protected override void OnCreate()
        {
            return;
            RenderPipelineManager.beginCameraRendering += OnBeforeRenderPipelineCull;
            Camera.onPreCull += OnBeforeCull;
            instanceRendererSystem = World.GetOrCreateSystemManaged<InstanceRenderingSystem>();
        }

        protected override void OnUpdate()
        {
        }
        
        public void OnBeforeRenderPipelineCull(ScriptableRenderContext context, Camera camera)
        {
            return;
            OnBeforeCull(camera);
        }

        public void OnBeforeCull(Camera camera)
        {
            return;
            instanceRendererSystem.ActiveCamera = camera;
            instanceRendererSystem.Tick();
            instanceRendererSystem.ActiveCamera = null;

        }
    }
}
