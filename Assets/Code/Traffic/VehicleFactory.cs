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
    public class VehicleFactory {

         private static EntityArchetype _vehicleArchetype;
         private static int _vehicleCount;
         private static List<Mesh> _templateMesh;
         private static List<Material> _templateMaterial;
         private static List<float> _weights;
         private static List<float3> _bounds;
         private static int _templateLength;
         private static Dictionary<string, int> _templateNameId;

        public static void Init(EntityManager manager)
        {
            _vehicleArchetype = manager.CreateArchetype(typeof(VehicleData), typeof(Position), typeof(Rotation),
                typeof(HitResult), typeof(RenderMesh), typeof(BVHAABB));
            _vehicleCount = 0;

            RoadGraph.Instance.InitRandom();

            _templateMesh = new List<Mesh>();
            _templateMaterial = new List<Material>();
            _weights = new List<float>();
            _bounds = new List<float3>();
            _templateNameId = new Dictionary<string, int>();

            _templateLength = TrafficConfig.Instance.Templates.Count;
            float weightSum = 0;
            TrafficConfig.Instance.Templates.ForEach( templ =>
            {
                _templateMesh.Add(templ.Prefab.GetComponent<MeshFilter>().sharedMesh);
                _templateMaterial.Add(templ.Prefab.GetComponent<MeshRenderer>().sharedMaterial);
                Bounds renderBound = templ.Prefab.GetComponent<MeshRenderer>().bounds;
                float width = renderBound.extents.x > renderBound.extents.z ?
                    renderBound.extents.x :
                    renderBound.extents.z;
                float height = renderBound.extents.y;
                float3 localBound = new float3(width, height, width);
                _bounds.Add(localBound);
                _weights.Add(templ.Weight);
                weightSum += templ.Weight;
                _templateNameId.Add(templ.Prefab.name, _templateMesh.Count - 1);
            });
            weightSum = weightSum < 0.001f ? 1 : weightSum;
            _weights = _weights.ConvertAll(val => val / weightSum);

            for (int i = 1; i < _templateLength; i++)
            {
                _weights[i] += _weights[i - 1];
            }
        }

        public static Entity AddVehicle(EntityManager manager, string name)
        {
            throw new NotImplementedException();
            /*int templateId;
            if (_templateNameId.TryGetValue(name, out templateId))
            {

            }
            return Entity.Null;*/
        }

        public static Entity AddVehicle(EntityManager manager)
        {
            float3 pos, forward; quaternion rot;
            int currentid, lane;
            float speed, lerpPos, dir;
            RoadGraph.Instance.GetNextRandomPosition(out pos, out rot, out forward, out speed, out currentid, out lerpPos, out dir, out lane);

            float templateIdRandom = RoadGraph.Instance.RandomGen.NextFloat(1.0f);
            int templateId = 0;
            for (int i = 0; i < _templateLength; i++)
            {
                if (templateIdRandom > _weights[i])
                {
                    templateId = i + 1;
                }
            }

            var car = manager.CreateEntity(_vehicleArchetype);
            
            manager.SetComponentData(car, new VehicleData((uint)_vehicleCount, currentid, speed, forward, lerpPos, dir, pos, lane, 50.0f));
            manager.SetComponentData(car, new Position() { Value = pos });
            manager.SetComponentData(car, new Rotation() { Value = rot });
            manager.SetComponentData(car, new HitResult() { HitResultPacked = 0, FrontHitDistance = 50.0f });
            manager.SetComponentData(car, new BVHAABB() { Min = pos - _bounds[templateId], Max =  pos + _bounds[templateId] });
            manager.SetSharedComponentData(car, new RenderMesh()
            {
                castShadows = ShadowCastingMode.Off,
                material = _templateMaterial[templateId],
                mesh = _templateMesh[templateId],
                receiveShadows = false,
                subMesh = 0
            });

            _vehicleCount++;
            return car;
        }
    }   
}

