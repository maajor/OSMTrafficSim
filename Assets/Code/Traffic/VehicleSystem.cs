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

        struct VehicleGroup
        {
            public readonly int Length;
            public EntityArray Entity;
            public ComponentDataArray<VehicleData> VehicleData;
            public ComponentDataArray<AABB> AABB;
            public ComponentDataArray<Position> Position;
            public ComponentDataArray<Rotation> Rotation;
            public ComponentDataArray<HitResult> HitResult;
        }
        [Inject] VehicleGroup _vehicleGroup;
        
        private int Capacity = 1024;

        #region ComponentSystem Interface

        protected override void OnCreateManager()
        {
            Capacity = TrafficConfig.Instance.MaxVehicles;

            VehicleFactory.Init(EntityManager);
            for (var j = 0; j < Capacity; j++)
            {
                VehicleFactory.AddVehicle(EntityManager);
            }

            _roadSegments = new NativeArray<RoadSegment>(RoadGraph.Instance.RoadSegments.ToArray(), Allocator.Persistent);
            _roadNodes = new NativeArray<RoadNode>(RoadGraph.Instance.RoadNodes.ToArray(), Allocator.Persistent);
            _roadNodes.CopyFrom(RoadGraph.Instance.RoadNodes.ToArray());
            _roadSegments.CopyFrom(RoadGraph.Instance.RoadSegments.ToArray());

            var seeds = Utils.GetRandomSeed(Capacity);
            _randSeed = new NativeArray<uint>(seeds, Allocator.Persistent);
            _randSeed.CopyFrom(seeds);

            _BVH = new BVHConstructor(Capacity);
        }
        protected override void OnDestroyManager()
        {
            _roadNodes.Dispose();
            _roadSegments.Dispose();
            _randSeed.Dispose();
            _BVH.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            deps = _BVH.Calculate(deps, _vehicleGroup.AABB);

            var senseJob = new SenseEnvironmentJob()
            {
                VehicleData = _vehicleGroup.VehicleData,
                BVHArray = _BVH.BVHArray,
                HitResult = _vehicleGroup.HitResult,
                HalfBVHArrayLength = _BVH.BVHArray.Length / 2,
            };

            deps = senseJob.Schedule(Capacity, 64, deps);
            deps.Complete();
            //RoadGraph.Instance.VehicleData = _vehicleGroup.VehicleData;
            //RoadGraph.Instance.hit = _vehicleGroup.HitResult;
            DebugUtils.DebugConnection(_vehicleGroup.Position, _vehicleGroup.HitResult);

            var vehicleMoveJob = new VehicleMoveJob()
            {
                Positions = _vehicleGroup.Position,
                Rotations = _vehicleGroup.Rotation,
                VehicleData = _vehicleGroup.VehicleData,
                RoadNodes = _roadNodes,
                RoadSegments = _roadSegments,
                RandSeed = _randSeed,
                HitResult = _vehicleGroup.HitResult,
                AABB = _vehicleGroup.AABB,
                FrameSeed = (uint)Time.frameCount,
                DeltaTime = Time.deltaTime
            };
            deps = vehicleMoveJob.Schedule(Capacity, 64, deps);

            return deps;
        }
        #endregion

        #region JobDefinitions

        [BurstCompile]
        struct SenseEnvironmentJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<BVHNode> BVHArray;

            [ReadOnly]
            [NativeDisableParallelForRestriction]
            public ComponentDataArray<VehicleData> VehicleData;

            [NativeDisableParallelForRestriction]
            public ComponentDataArray<HitResult> HitResult;

            public int HalfBVHArrayLength;
            

            void QueryBVHNode(int comparedToNode, int leafNodeIndex, float3 forward, float3 position)
            {
                float3 comparecenter = (BVHArray[comparedToNode].aabb.Max + BVHArray[comparedToNode].aabb.Min) / 2.0f;
                float3 toward = (comparecenter - position);
                float manhantanDist = math.abs(toward.x) + math.abs(toward.z);
                bool is_overlap = Utils.AABBToAABBOverlap(BVHArray[comparedToNode].aabb, BVHArray[leafNodeIndex].aabb);

                if (BVHArray[comparedToNode].IsValid > 0 &&
                    (is_overlap || manhantanDist < 200.0f))
                {
                    // leaf node
                    if (BVHArray[comparedToNode].LeftNodeIndex < 0)
                    {
                        float distance = math.distance(comparecenter, position);
                        toward /= distance;
                        float angle = math.dot(toward, forward);
                        float absangle = math.abs(angle);
                        bool is_front = distance < 50.0f && angle > 0.999f;
                        bool is_side = distance < 5.0f && absangle > 0.1f;
                        int entityid = BVHArray[leafNodeIndex].EntityId;
                        if (is_front)
                        {
                            if (distance < HitResult[entityid].FrontHitDistance)
                            {
                                HitResult[entityid] = new HitResult()
                                {
                                    FrontHitDistance = distance,
                                    HitResultPacked =
                                        (HitResult[entityid].HitResultPacked | 0x1),
                                    FrontEntityId = BVHArray[comparedToNode].EntityId
                                };
                            }
                        }

                        if (is_side)
                        {
                            HitResult[entityid] = new HitResult()
                            {
                                FrontHitDistance = HitResult[entityid].FrontHitDistance,
                                HitResultPacked =
                                    (HitResult[entityid].HitResultPacked | (angle > 0 ? 0x2 : 0x4))
                            };
                        }
                    }
                    else
                    {
                        int left = BVHArray[comparedToNode].LeftNodeIndex;
                        int right = BVHArray[comparedToNode].RightNodeIndex;
                        if (left != leafNodeIndex) QueryBVHNode(left, leafNodeIndex, forward, position);
                        if (right != leafNodeIndex)  QueryBVHNode(right, leafNodeIndex, forward, position);
                    }
                }
            }

            public void Execute(int i)
            {
                int entityindex = BVHArray[HalfBVHArrayLength + i].EntityId;
                HitResult[entityindex] = new HitResult() { FrontHitDistance = 50.0f, HitResultPacked = 0 };
                float3 forward = VehicleData[entityindex].Forward;
                float3 center = VehicleData[entityindex].Position;
                QueryBVHNode(0, HalfBVHArrayLength + i, forward, center);
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

            public ComponentDataArray<AABB> AABB;

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

                if (newDistanceAhead < 20.0f)
                {
                    newSpeed -= 0.5f;
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

                    AABB newAABB = AABB[i];
                    newAABB.Max += currentDir * stepLength;
                    newAABB.Min += currentDir * stepLength;
                    AABB[i] = newAABB;

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

                    AABB newAABB = AABB[i];
                    newAABB.Max += (nextPos - currentPos);
                    newAABB.Min += (nextPos - currentPos);
                    AABB[i] = newAABB;

                    Positions[i] = new Position() { Value = nextPos };
                    Rotations[i] = new Rotation() { Value = newRot };

                    UnsafeUtility.Free(_availableSeg, Allocator.Temp);
                    _availableSeg = null;
                }
            }
        }

        #endregion

        private BVHConstructor _BVH;
        private NativeArray<RoadNode> _roadNodes;
        private NativeArray<RoadSegment> _roadSegments;
        private NativeArray<uint> _randSeed;
    }
}
