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
    public partial class TrafficLightSystem : SystemBase
    {
        private bool running = true;
        public void Pause()
        {
            running = false;
        }
        public void Restart()
        {
            OnDestroy();
            OnCreate();
        }
        protected override void OnCreate()
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

            running = true;
        }

        protected override void OnUpdate()
        {
            if (!running) return;
            var trafficLight = new TrafficLightJob()
            {
                DeltaTime = World.Time.DeltaTime
            };
            trafficLight.Schedule();
        }
        
        [BurstCompile]
        public partial struct TrafficLightJob : IJobEntity//IJobForEach<RoadNode>
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
