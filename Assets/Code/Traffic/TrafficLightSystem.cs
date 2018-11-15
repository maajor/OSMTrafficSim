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
        [Inject] private RoadSegmentGroup _roadSegmentGroup;
        [Inject] private RoadNodeGroup _roadNodeGroup;

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
                RoadNodes = _roadNodeGroup.RoadNodes,
                DeltaTime = Time.deltaTime
            };
            return trafficLight.Schedule(_roadNodeGroup.RoadNodes.Length, 32, deps);
        }
        
        [BurstCompile]
        public struct TrafficLightJob : IJobParallelFor
        {
            public ComponentDataArray<RoadNode> RoadNodes;
            public float DeltaTime;

            public void Execute(int index)
            {
                RoadNode currentnode = RoadNodes[index];
                float newCd = currentnode.CountDown - DeltaTime;
                if (newCd < 0)
                {
                    newCd = 20.0f;
                    int nextConnect = (currentnode.ActiveConnection + 1) % 3;
                    int count = 0;
                    while (currentnode.ConnectionSegIds[nextConnect].x == -1 && currentnode.ConnectionSegIds[nextConnect].y == -1)
                    {
                        if (count > 3)
                        {
                            nextConnect = 0;
                            break;
                        }
                        nextConnect = (nextConnect + 1) % 3;
                        count++;
                    }
                    currentnode.ActiveConnection = nextConnect;
                }

                currentnode.CountDown = newCd;
                RoadNodes[index] = currentnode;
            }
        }
    }
}
