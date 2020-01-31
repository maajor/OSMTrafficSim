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
    public class InstanceRendererBootstrap : ComponentSystem {

        InstanceRenderingSystem instanceRendererSystem;

        protected override void OnCreate()
        {
            RenderPipelineManager.beginCameraRendering += OnBeforeRenderPipelineCull;
            Camera.onPreCull += OnBeforeCull;
            instanceRendererSystem = this.World.GetOrCreateSystem<InstanceRenderingSystem>();
        }

        protected override void OnUpdate()
        {
        }
        
        public void OnBeforeRenderPipelineCull(ScriptableRenderContext context, Camera camera)
        {
            OnBeforeCull(camera);
        }

        public void OnBeforeCull(Camera camera)
        {
            instanceRendererSystem.ActiveCamera = camera;
            instanceRendererSystem.Tick();
            instanceRendererSystem.ActiveCamera = null;

        }
    }
}
