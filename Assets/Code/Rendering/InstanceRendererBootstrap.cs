using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
//using Unity.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OSMTrafficSim
{
    
public class InstanceRendererBootstrap : ComponentSystem {

    protected override void OnCreateManager()
    {
        RenderPipeline.beginCameraRendering += OnBeforeCull;
        Camera.onPreCull += OnBeforeCull;
    }

    protected override void OnUpdate()
    {
    }

    [Inject]
#pragma warning disable 649
    InstanceRenderingSystem instanceRendererSystem;
    
    public void OnBeforeCull(Camera camera)
    {
        instanceRendererSystem.ActiveCamera = camera;
        instanceRendererSystem.Tick();
        instanceRendererSystem.ActiveCamera = null;

    }
}

}
