using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace OSMTrafficSim
{
    public class VehicleSystem : JobComponentSystem
    {
        ComponentGroup _template;
        ComponentGroup _vehicleGroup;
        EntityArchetype _carArchitype;
        private const int Capacity = 102400;

        private ComponentDataArray<Position> _position;
        private ComponentDataArray<Rotation> _rotation;
        private ComponentDataArray<VehicleData> _vehicleData;
        private NativeArray<RoadNode> _roadNodes;
        private NativeArray<RoadSegment> _roadSegments;
        private NativeArray<uint> _randSeed;

        protected override void OnStartRunning() {
            Debug.Log("Create Traffic System");
            RoadGraph.Instance.InitRandom();

            _vehicleGroup = GetComponentGroup(typeof(VehicleData), typeof(Position), typeof(Rotation));
            _template = GetComponentGroup(typeof(VehicleTemplate));

            var iterator = _template.GetEntityArray();
            var entities = new NativeArray<Entity>(iterator.Length, Allocator.Temp);
            iterator.CopyTo(entities);

            _carArchitype = EntityManager.CreateArchetype(
                typeof(VehicleData), typeof(Position), typeof(Rotation), typeof(MeshInstanceRenderer)
            );
            
            List<uint> randSeeds= new List<uint>();
            for (var j = 0; j < Capacity; j++)
            {
                var car = EntityManager.CreateEntity(_carArchitype);
                float3 pos, forward; quaternion rot;
                int currentid;
                float speed, lerpPos, dir;
                RoadGraph.Instance.GetNextRandomPosition(out pos, out rot, out forward, out speed, out currentid, out lerpPos, out dir);
                EntityManager.SetComponentData(car, new VehicleData((uint)j, currentid, speed, forward, lerpPos, dir, pos));
                EntityManager.SetComponentData(car, new Position() { Value = pos});
                EntityManager.SetComponentData(car, new Rotation() { Value = rot});
                int rendererid = RoadGraph.Instance.RandomGen.NextInt(0, entities.Length);
                EntityManager.SetSharedComponentData(car, EntityManager.GetSharedComponentData<VehicleTemplate>(entities[rendererid]).renderer);
                randSeeds.Add(RoadGraph.Instance.RandomGen.NextUInt(1000000));
            }
            entities.Dispose();

            _roadSegments = new NativeArray<RoadSegment>(RoadGraph.Instance.RoadSegments.ToArray(), Allocator.Persistent);
            _roadNodes = new NativeArray<RoadNode>(RoadGraph.Instance.RoadNodes.ToArray(), Allocator.Persistent);
            _roadNodes.CopyFrom(RoadGraph.Instance.RoadNodes.ToArray());
            _roadSegments.CopyFrom(RoadGraph.Instance.RoadSegments.ToArray());

            _randSeed = new NativeArray<uint>(randSeeds.ToArray(), Allocator.Persistent);
            _randSeed.CopyFrom(randSeeds.ToArray());
        }

        [BurstCompile]
        struct VehicleMoveJob : IJobParallelFor
        {
            public float DeltaTime;
            public uint FrameSeed;

            [ReadOnly]
            public NativeArray<RoadNode> RoadNodes;
            
            [ReadOnly]
            public NativeArray<RoadSegment> RoadSegments;

            [ReadOnly]
            public NativeArray<uint> RandSeed;

            [NativeDisableParallelForRestriction]
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<Position> Positions;

            [NativeDisableParallelForRestriction]
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<Rotation> Rotations;

            [NativeDisableParallelForRestriction]
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<VehicleData> VehicleData;

            public unsafe void Execute(int i)
            {
                float3 currentPos = Positions[i].Value;
                float3 currentDir = VehicleData[i].Forward;
                float stepLength = VehicleData[i].Speed * DeltaTime;

                float segLength = RoadSegments[VehicleData[i].SegId].Length;
                float stepPerc = stepLength / segLength;
                float nextPosPerc = VehicleData[i].CurrentSegPos + stepPerc * VehicleData[i].Direction;
                //great, still in this seg
                if (nextPosPerc < 1.0f && nextPosPerc > 0.0f)
                {
                    float3 nextPos = currentPos + currentDir * stepLength;
                    VehicleData[i] = new VehicleData(
                        VehicleData[i].Id,
                        VehicleData[i].SegId,
                        VehicleData[i].Speed,
                        VehicleData[i].Forward,
                        nextPosPerc,
                        VehicleData[i].Direction,
                        nextPos
                        );
                    
                    Positions[i] = new Position() { Value = nextPos };
                }
                //reach endnode, find next seg
                else
                {
                    Unity.Mathematics.Random rdGen = new Unity.Mathematics.Random();
                    rdGen.InitState(FrameSeed + RandSeed[i]);
                    int currentSeg = VehicleData[i].SegId;
                    int reachedNode = VehicleData[i].Direction > 0.0f ? RoadSegments[currentSeg].EndNodeId : RoadSegments[currentSeg].StartNodeId;

                    //find next available segment
                    int* _availableSeg = (int*)UnsafeUtility.Malloc(
                        5 * sizeof(int), sizeof(int), Allocator.Temp);
                    int availableSegCount = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        int seg1 = RoadNodes[reachedNode].ConnectionSegIds[k].x;
                        int seg2 = RoadNodes[reachedNode].ConnectionSegIds[k].y;
                        if (seg1 != -1 && seg1 != currentSeg && //not current seg, and next seg not one-way
                            !(RoadSegments[seg1].EndNodeId == reachedNode && RoadSegments[seg1].IsOneWay == 1)) _availableSeg[availableSegCount++] = seg1;
                        if (seg2 != -1 && seg2 != currentSeg && //not current seg, and next seg not one-way
                            !(RoadSegments[seg2].EndNodeId == reachedNode && RoadSegments[seg2].IsOneWay == 1)) _availableSeg[availableSegCount++] = seg2;
                    }

                    int nextSeg = currentSeg;
                    if (availableSegCount > 0)
                    {
                        int selectSegId = rdGen.NextInt(0, availableSegCount - 1);
                        nextSeg = _availableSeg[selectSegId];
                    }

                    float dir = RoadSegments[nextSeg].StartNodeId == reachedNode ? 1.0f : -1.0f;
                    float3 nextForward = RoadSegments[nextSeg].Direction * dir;
                    quaternion newRot = quaternion.LookRotation(nextForward, new float3(){x=0,y=1,z=0});

                    float nextPerct = dir > 0.0f
                        ? (nextPosPerc > 0.0f ? nextPosPerc - 1.0f : -nextPosPerc)
                        : (nextPosPerc > 0.0f ? 2.0f - nextPosPerc : nextPosPerc + 1.0f);

                    float3 nextPos = (1.0f - nextPerct) * RoadNodes[RoadSegments[nextSeg].StartNodeId].Position +
                                     (nextPerct) * RoadNodes[RoadSegments[nextSeg].EndNodeId].Position;

                    VehicleData[i] = new VehicleData(
                        VehicleData[i].Id,
                        nextSeg,
                        VehicleData[i].Speed,
                        nextForward,
                        nextPerct,
                        dir,
                        nextPos
                    );
                    Positions[i] = new Position() { Value = nextPos };
                    Rotations[i] = new Rotation() { Value = newRot };

                    UnsafeUtility.Free(_availableSeg, Allocator.Temp);
                    _availableSeg = null;
                }
            }
        }

        protected override void OnDestroyManager()
        {
            _roadNodes.Dispose();
            _roadSegments.Dispose();
            _randSeed.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            
            _position = _vehicleGroup.GetComponentDataArray<Position>();
            _rotation = _vehicleGroup.GetComponentDataArray<Rotation>();
            _vehicleData = _vehicleGroup.GetComponentDataArray<VehicleData>();

            //RoadGraph.Instance.VehicleData = _vehicleData;

            var vehicleMoveJob = new VehicleMoveJob()
            {
                Positions = _position,
                Rotations = _rotation,
                VehicleData = _vehicleData,
                RoadNodes = _roadNodes,
                RoadSegments = _roadSegments,
                RandSeed = _randSeed
            };
            vehicleMoveJob.FrameSeed = (uint) Time.frameCount;
            vehicleMoveJob.DeltaTime = Time.deltaTime;
            deps = vehicleMoveJob.Schedule(Capacity, 64, deps);

            return deps;
        }

        
    }
}
