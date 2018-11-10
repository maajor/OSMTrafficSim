using OSMTrafficSim.BVH;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{

    public class PedestrianFactory
    {
        private static EntityArchetype _pedestrianArchetype;
       // private static int _pedestrianCount;

        public static void Init(EntityManager manager)
        {
            _pedestrianArchetype = manager.CreateArchetype(typeof(PedestrianData), typeof(Position), typeof(Rotation),
                 typeof(MeshInstanceRenderer));
            //_pedestrianCount = 0;

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
            manager.SetSharedComponentData(pedestrian, new MeshInstanceRenderer()
            {
                castShadows = ShadowCastingMode.Off,
                material = TrafficConfig.Instance.ManMat,
                mesh = TrafficConfig.Instance.ManMesh,
                receiveShadows = false,
                subMesh = 0
            });

            //_pedestrianCount++;
            return pedestrian;
        }
    }
}
