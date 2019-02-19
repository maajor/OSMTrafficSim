using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
//using Unity.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OSMTrafficSim
{
    public class InstanceRendererBootstrap : ComponentSystem {

        InstanceRenderingSystem instanceRendererSystem;

        protected override void OnCreateManager()
        {
            RenderPipeline.beginCameraRendering += OnBeforeCull;
            Camera.onPreCull += OnBeforeCull;
            instanceRendererSystem = this.World.GetOrCreateManager<InstanceRenderingSystem>();
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
