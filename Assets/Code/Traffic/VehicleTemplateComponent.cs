using System.Collections;
using System.Collections.Generic;
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

namespace OSMTrafficSim
{
    [Serializable]
    public struct VehicleTemplate : ISharedComponentData
    {
        public MeshInstanceRenderer renderer;

        public VehicleTemplate(MeshInstanceRenderer renderer)
        {
            this.renderer = renderer;
        }
    }

    public class VehicleTemplateComponent : SharedComponentDataWrapper<VehicleTemplate>
    {

        public VehicleTemplateComponent(MeshInstanceRenderer renderer)
        {
            Value = new VehicleTemplate(renderer);
        }
    }
}