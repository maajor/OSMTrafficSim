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
        private int Capacity = 1024;

        #region ComponentSystem Interface

        protected override void OnStartRunning() {
            Debug.Log("Create Traffic System");
            RoadGraph.Instance.InitRandom();

            _vehicleGroup = GetComponentGroup(typeof(VehicleData), typeof(Position), typeof(Rotation), typeof(HitResult));
            _template = GetComponentGroup(typeof(VehicleTemplate));

            var iterator = _template.GetEntityArray();
            var entities = new NativeArray<Entity>(iterator.Length, Allocator.Temp);
            iterator.CopyTo(entities);
            
            Capacity = TrafficConfig.Instance.MaxVehicles;
            List<uint> randSeeds= new List<uint>();
            for (var j = 0; j < Capacity; j++)
            {

                float3 pos, forward; quaternion rot;
                int currentid, lane;
                float speed, lerpPos, dir;
                RoadGraph.Instance.GetNextRandomPosition(out pos, out rot, out forward, out speed, out currentid, out lerpPos, out dir, out lane);
                int rendererid = RoadGraph.Instance.RandomGen.NextInt(0, entities.Length);
                VehicleTemplate vehicleTemplateData =
                    EntityManager.GetSharedComponentData<VehicleTemplate>(entities[rendererid]);

                //We need physics, so a hybrid way, instanciate gameobjects.
                GameObject newgo = new GameObject("car", typeof(VehicleComponent), typeof(HitResultComponent), typeof(PositionComponent), typeof(RotationComponent), typeof(MeshInstanceRendererComponent));
                newgo.hideFlags = HideFlags.HideInHierarchy;
                newgo.layer = 9;
                BoxCollider col = newgo.AddComponent<BoxCollider>();
                col.size = vehicleTemplateData.ColliderSize;
                col.center = vehicleTemplateData.ColliderCenter;
                GameObjectEntity this_entity = newgo.GetComponent<GameObjectEntity>();
                var car = this_entity.Entity;
                
                EntityManager.SetComponentData(car, new VehicleData((uint)j, currentid, speed, forward, lerpPos, dir, pos, lane, 50.0f));
                EntityManager.SetComponentData(car, new Position() { Value = pos});
                EntityManager.SetComponentData(car, new Rotation() { Value = rot});
                EntityManager.SetComponentData(car, new HitResult() { HitResultPacked = 0, FrontHitDistance = 50.0f});
                EntityManager.SetSharedComponentData(car, vehicleTemplateData.renderer);
                
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
            _hitResult = _vehicleGroup.GetComponentDataArray<HitResult>();

            //RoadGraph.Instance.VehicleData = _vehicleData;
            
            var commands = new NativeArray<RaycastCommand>(Capacity , Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(Capacity , Allocator.TempJob);

            // 1: Setup Raycast for environment sensing
            var setupRaycastJob = new SetupRaycastJob
            {
                Commands = commands,
                VehicleData = _vehicleData,
            };
            deps = setupRaycastJob.Schedule(Capacity, 64, deps);

            // 2: Raycast jobs
            deps = RaycastCommand.ScheduleBatch(commands, hits, 64, deps);

            // 3: Transfer raycast result to vehicledata
            var transferJob = new TransferRaycastResultJob
            {
                Commands = commands,
                RaycastHits = hits,
                HitResult = _hitResult
            };
            deps = transferJob.Schedule(Capacity, 64, deps);

            // 4: move vehicle
            var vehicleMoveJob = new VehicleMoveJob()
            {
                Positions = _position,
                Rotations = _rotation,
                VehicleData = _vehicleData,
                RoadNodes = _roadNodes,
                RoadSegments = _roadSegments,
                RandSeed = _randSeed,
                HitResult = _hitResult
            };
            vehicleMoveJob.FrameSeed = (uint) Time.frameCount;
            vehicleMoveJob.DeltaTime = Time.deltaTime;
            deps = vehicleMoveJob.Schedule(Capacity, 64, deps);

            return deps;
        }
        #endregion
        
        #region JobDefinitions

        [BurstCompile]
        struct SetupRaycastJob : IJobParallelFor
        {
            [WriteOnly]
            public NativeArray<RaycastCommand> Commands;

            [ReadOnly]
            public ComponentDataArray<VehicleData> VehicleData;

            public unsafe void Execute(int i)
            {
                Commands[i] = new RaycastCommand(VehicleData[i].Position + 5.0f * VehicleData[i].Forward, VehicleData[i].Forward, 30.0f);
            }
        }
        
        [BurstCompile]
        struct TransferRaycastResultJob : IJobParallelFor
        {
            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<RaycastHit> RaycastHits;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<RaycastCommand> Commands;
            
            [WriteOnly]
            public ComponentDataArray<HitResult> HitResult;
            
            public unsafe void Execute(int i)
            {
                int result = 0;
                if (RaycastHits[i].distance < 30.0f && RaycastHits[i].distance> 1.0f) result |= 0x1;
                
                HitResult[i] = new HitResult { HitResultPacked = result, FrontHitDistance = RaycastHits[i].distance};
            }
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

            [ReadOnly]
            public ComponentDataArray<HitResult> HitResult;
            
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<Position> Positions;
            
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<Rotation> Rotations;
            
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<VehicleData> VehicleData;

            public unsafe void Execute(int i)
            {
                float3 currentPos = Positions[i].Value;
                float3 currentDir = VehicleData[i].Forward;

                #region SpeedVariation
                float prevDistanceAhead = VehicleData[i].HitDistAhead;
                float newDistanceAhead = HitResult[i].FrontHitDistance;
                float distAheadDiff = prevDistanceAhead - newDistanceAhead;
                int hitResult = HitResult[i].HitResultPacked;
                float maxSpeed = RoadSegments[VehicleData[i].SegId].MaxSpeed;
                float newSpeed = VehicleData[i].Speed;
                if (hitResult == 0 && newSpeed < maxSpeed)
                {
                    newSpeed += 0.5f;
                }else if ((hitResult & 0x1) == 1 && (distAheadDiff > 0))
                {
                    newSpeed -= (distAheadDiff / 2.0f);
                }
                newSpeed = newSpeed > maxSpeed ? maxSpeed : newSpeed;
                newSpeed = newSpeed < 0 ? 0 : newSpeed;
                #endregion

                float stepLength = newSpeed * DeltaTime;
                float segLength = RoadSegments[VehicleData[i].SegId].Length;
                float stepPerc = stepLength / segLength;
                float nextPosPerc = VehicleData[i].CurrentSegPos + stepPerc * VehicleData[i].Direction;

                Unity.Mathematics.Random rdGen = new Unity.Mathematics.Random();
                rdGen.InitState(FrameSeed + RandSeed[i]);

                #region switchLane

                int laneCount = RoadSegments[VehicleData[i].SegId].LaneNumber;
                int currentLane = VehicleData[i].Lane;
                int laneSwitch = rdGen.NextInt(-10, 10);
                laneSwitch = laneSwitch / 10;
                if (currentLane == 0 && laneSwitch == -1) laneSwitch = 0;
                if (currentLane == (laneCount - 1) && laneSwitch == 1) laneSwitch = 0;
                int nextLane = VehicleData[i].Lane + laneSwitch;
                #endregion

                //great, still in this seg
                if (nextPosPerc < 1.0f && nextPosPerc > 0.0f)
                {
                    //step forward
                    float3 nextPos = currentPos + currentDir * stepLength;
                    //offset lane
                    nextPos += laneSwitch * VehicleData[i].Direction * RoadSegments[VehicleData[i].SegId].LaneWidth *
                               RoadSegments[VehicleData[i].SegId].RightDirection;
                    VehicleData[i] = new VehicleData(
                        VehicleData[i].Id,
                        VehicleData[i].SegId,
                        newSpeed,
                        VehicleData[i].Forward,
                        nextPosPerc,
                        VehicleData[i].Direction,
                        nextPos,
                        nextLane,
                        newDistanceAhead
                        );
                    
                    Positions[i] = new Position() { Value = nextPos };
                }
                //reach end node, find next seg
                else
                {
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
                    float dir = 1.0f;
                    if (availableSegCount > 0)//luckily we can proceed
                    {
                        int selectSegId = rdGen.NextInt(0, availableSegCount);
                        nextSeg = _availableSeg[selectSegId];
                        dir = RoadSegments[nextSeg].StartNodeId == reachedNode ? 1.0f : -1.0f;
                    }
                    else//to the end, spawn a new pos
                    {
                        nextSeg = rdGen.NextInt(0, RoadSegments.Length);
                    }

                    float3 nextForward = RoadSegments[nextSeg].Direction * dir;
                    quaternion newRot = quaternion.LookRotation(nextForward, new float3(){x=0,y=1,z=0});

                    laneCount = RoadSegments[nextSeg].LaneNumber;
                    nextLane = nextLane < laneCount ? nextLane : laneCount - 1;
                    float laneOffset = (nextLane + 0.5f) * RoadSegments[nextSeg].LaneWidth;
                    if (RoadSegments[nextSeg].IsOneWay == 1)
                    {
                        laneOffset -= (laneCount / 2.0f) * RoadSegments[nextSeg].LaneWidth;
                    }

                    float nextPerct = dir > 0.0f
                        ? (nextPosPerc > 0.0f ? nextPosPerc - 1.0f : -nextPosPerc)
                        : (nextPosPerc > 0.0f ? 2.0f - nextPosPerc : nextPosPerc + 1.0f);

                    float3 nextPos = (1.0f - nextPerct) * RoadNodes[RoadSegments[nextSeg].StartNodeId].Position +
                                     (nextPerct) * RoadNodes[RoadSegments[nextSeg].EndNodeId].Position;
                    nextPos += laneOffset * dir * RoadSegments[nextSeg].RightDirection;

                    VehicleData[i] = new VehicleData(
                        VehicleData[i].Id,
                        nextSeg,
                        newSpeed,
                        nextForward,
                        nextPerct,
                        dir,
                        nextPos,
                        nextLane,
                        newDistanceAhead
                    );
                    Positions[i] = new Position() { Value = nextPos };
                    Rotations[i] = new Rotation() { Value = newRot };

                    UnsafeUtility.Free(_availableSeg, Allocator.Temp);
                    _availableSeg = null;
                }
            }
        }

        #endregion
        
        private ComponentDataArray<Position> _position;
        private ComponentDataArray<Rotation> _rotation;
        private ComponentDataArray<VehicleData> _vehicleData;
        private ComponentDataArray<HitResult> _hitResult;
        private NativeArray<RoadNode> _roadNodes;
        private NativeArray<RoadSegment> _roadSegments;
        private NativeArray<uint> _randSeed;
    }
}
