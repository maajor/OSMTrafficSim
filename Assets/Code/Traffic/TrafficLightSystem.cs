using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace OSMTrafficSim
{
    [UpdateBefore(typeof(VehicleSystem))]
    public class TrafficLightSystem : JobComponentSystem
    {
        protected override void OnCreateManager()
        {
            var _roadNodeArchetype = EntityManager.CreateArchetype(typeof(RoadNode));
            var _roadSegmentArchetype = EntityManager.CreateArchetype(typeof(RoadSegment));
            foreach (var nodes in RoadGraph.Instance.RoadNodes)
            {
                var entity = EntityManager.CreateEntity(_roadNodeArchetype);
                EntityManager.SetComponentData(entity, nodes);
            }
            foreach (var segs in RoadGraph.Instance.RoadSegments)
            {
                var entity = EntityManager.CreateEntity(_roadSegmentArchetype);
                EntityManager.SetComponentData(entity, segs);
            }
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            var trafficLight = new TrafficLightJob()
            {
                DeltaTime = Time.deltaTime
            };
            return trafficLight.Schedule(this, deps);
        }
        
        [BurstCompile]
        public struct TrafficLightJob : IJobProcessComponentData<RoadNode>
        {
            public float DeltaTime;

            public void Execute(ref RoadNode roadnode)
            {
                float newCd = roadnode.CountDown - DeltaTime;
                if (newCd < 0)
                {
                    newCd = 20.0f;
                    int nextConnect = (roadnode.ActiveConnection + 1) % 3;
                    int count = 0;
                    while (roadnode.ConnectionSegIds[nextConnect].x == -1 && roadnode.ConnectionSegIds[nextConnect].y == -1)
                    {
                        if (count > 3)
                        {
                            nextConnect = 0;
                            break;
                        }
                        nextConnect = (nextConnect + 1) % 3;
                        count++;
                    }
                    roadnode.ActiveConnection = nextConnect;
                }

                roadnode.CountDown = newCd;
            }
        }
    }
}
