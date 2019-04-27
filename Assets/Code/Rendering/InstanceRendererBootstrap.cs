using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
//using Unity.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OSMTrafficSim
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class InstanceRendererBootstrap : ComponentSystem {

        InstanceRenderingSystem instanceRendererSystem;

        protected override void OnCreateManager()
        {
            RenderPipeline.beginCameraRendering += OnBeforeCull;
            Camera.onPreCull += OnBeforeCull;
            instanceRendererSystem = this.World.GetOrCreateSystem<InstanceRenderingSystem>();
        }

        protected override void OnUpdate()
        {
        }
        
        public void OnBeforeCull(Camera camera)
        {
            instanceRendererSystem.ActiveCamera = camera;
            instanceRendererSystem.Tick();
            instanceRendererSystem.ActiveCamera = null;

        }
    }
}
