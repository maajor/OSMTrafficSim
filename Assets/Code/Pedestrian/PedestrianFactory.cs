using OSMTrafficSim.BVH;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{

    public class PedestrianFactory
    {
        private static EntityArchetype _pedestrianArchetype;
        private static int _pedestrianCount;

        public static void Init(EntityManager manager)
        {
            _pedestrianArchetype = manager.CreateArchetype(typeof(PedestrianData), typeof(Position), typeof(Rotation),
                 typeof(InstanceRendererData), typeof(InstanceRendererProperty), typeof(PedestrianState));
            _pedestrianCount = 0;
            PedestrianArea.Instance.InitRandom();
        }

        public static Entity AddPedestrian(EntityManager manager)
        {
            float3 pos, forward; quaternion rot;
            float2 localpos;
            float speed;
            int2 gridid;
            PedestrianArea.Instance.GetNextRandomPosition(out pos, out rot, out forward, out localpos, out speed, out gridid);
            
            var pedestrian = manager.CreateEntity(_pedestrianArchetype);

            manager.SetComponentData(pedestrian, new PedestrianData(){ Forword = forward, LocalPos = localpos, Speed = speed, WorldPos = pos, GridId = gridid });
            manager.SetComponentData(pedestrian, new Position() { Value = pos });
            manager.SetComponentData(pedestrian, new Rotation() { Value = rot });
            manager.SetSharedComponentData(pedestrian, new InstanceRendererData()
            {
                CastShadows = ShadowCastingMode.Off,
                Material = TrafficConfig.Instance.PedestrianConfig.ManMat,
                Mesh = TrafficConfig.Instance.PedestrianConfig.ManMesh,
                ReceiveShadows = false,
                SubMesh = 0,
                CullDistance = TrafficConfig.Instance.PedestrianConfig.PedestrianCullDistance,
                InstanceShaderPropertyId = Shader.PropertyToID("_FrameRange")
            });
            manager.SetComponentData(pedestrian, new InstanceRendererProperty()
            {
                ParamId = Shader.PropertyToID("_FrameRange"),
                Value = new float4(26, 58, 0, 0)
            });
            manager.SetComponentData(pedestrian, new PedestrianState(){ State = 0 });

            _pedestrianCount++;
            return pedestrian;
        }
    }
}
