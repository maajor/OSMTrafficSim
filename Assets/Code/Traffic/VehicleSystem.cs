using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;

namespace OSMTrafficSim
{
    public class VehicleSystem : JobComponentSystem
    {
        private int _capacity = 1024;

        #region ComponentSystem Interface

        protected override void OnCreate()
        {
            _capacity = TrafficConfig.Instance.MaxVehicles;

            VehicleFactory.Init(EntityManager);
            for (var j = 0; j < _capacity; j++)
            {
                VehicleFactory.AddVehicle(EntityManager);
            }
            
            _bound = RoadGraph.Instance.BoundingBox;

            _BVH = new BVHConstructor(_capacity);
            _rdGens = Utils.GetRandomizerPerThread();

            //_vehicleGroup = EntityManager.CreateEntityQuery(typeof(VehicleData), typeof(BVHAABB), typeof(Translation),
            //    typeof(Rotation), typeof(HitResult));
            _vehicleGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<Translation>(),
                    ComponentType.ReadWrite<Rotation>(),
                    ComponentType.ReadWrite<VehicleData>(),
                    ComponentType.ReadWrite<BVHAABB>(),
                    ComponentType.ReadWrite<HitResult>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
            _roadSegmentGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RoadSegment>(),
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
            _roadNodeGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RoadNode>(),
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }
        protected override void OnDestroy()
        {
            _BVH.Dispose();
            _rdGens.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            //temp container, deallocated in jobs
            var vehicleAABB = _vehicleGroup.ToComponentDataArray<BVHAABB>(Allocator.TempJob);
            var vehicleData = _vehicleGroup.ToComponentDataArray<VehicleData>(Allocator.TempJob);
            var vehicleHitresultTemp = _vehicleGroup.ToComponentDataArray<HitResult>(Allocator.TempJob);

            deps = _BVH.Calculate(deps, vehicleAABB);

            //Sense surrounding vehicles, write to vehicleHitResultTemp, the writing limit exceed chunk, so write to a temp first.
            deps = new SenseEnvironmentJob()
            {
                VehicleData = vehicleData,
                BVHArray = _BVH.BVHArray,
                HitResult = vehicleHitresultTemp,
                HalfBVHArrayLength = _BVH.BVHArray.Length / 2,
            }.Schedule(_capacity, 64, deps);

            var hitResults = GetArchetypeChunkComponentType<HitResult>(false);

            //need to access entity id and write with id, dispatch a chunkjob
            deps = new WriteSenseResultJob()
            {
                HitResultTemp = vehicleHitresultTemp,
                HitResult = hitResults
            }.Schedule(_vehicleGroup, deps);

            //move according to sense result
            deps = new VehicleMoveJob()
            {
                RoadNodes = _roadNodeGroup.ToComponentDataArray<RoadNode>(Allocator.TempJob),
                RoadSegments = _roadSegmentGroup.ToComponentDataArray<RoadSegment>(Allocator.TempJob),
                FrameSeed = (uint)UnityEngine.Time.frameCount,
                DeltaTime = Time.DeltaTime,
                BoundingBox = _bound,
                RdGens = _rdGens
            }.Schedule(_vehicleGroup, deps);

            
            return deps;
        }
        #endregion

        #region Private Fields
        private BVHConstructor _BVH;
        private Bounds _bound;
        private NativeArray<Unity.Mathematics.Random> _rdGens;
        private EntityQuery _vehicleGroup;
        private EntityQuery _roadSegmentGroup;
        private EntityQuery _roadNodeGroup;
        #endregion

        #region Jobs In This System
        //place the job in system, otherwise debugger cannot find entities.
        [BurstCompile]
        struct VehicleMoveJob : IJobForEach<HitResult, Translation, Rotation, VehicleData, BVHAABB>
        {
            public float DeltaTime;
            public uint FrameSeed;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<RoadNode> RoadNodes;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<RoadSegment> RoadSegments;


            public Bounds BoundingBox;

            #pragma warning disable CS0649
            [NativeSetThreadIndex]
            int threadId;

            [NativeDisableParallelForRestriction]
            public NativeArray<Unity.Mathematics.Random> RdGens;

            public unsafe void Execute(ref HitResult thisHitResult, ref Translation translation, ref Rotation rotation, ref VehicleData vehicleData, ref BVHAABB bvhAabb)
            {
                var threadRandom = RdGens[threadId];

                float3 currentPos = translation.Value;
                float3 currentDir = vehicleData.Forward;

                int laneCount, nextLane;
                //spawn new pos
                if (!BoundingBox.Contains(currentPos))
                {
                    int nextSeg = threadRandom.NextInt(0, RoadSegments.Length);

                    float3 nextForward = RoadSegments[nextSeg].Direction;
                    quaternion newRot = quaternion.LookRotation(nextForward, new float3() { x = 0, y = 1, z = 0 });

                    laneCount = RoadSegments[nextSeg].LaneNumber;
                    nextLane = 0;
                    float laneOffset = (nextLane + 0.5f) * RoadSegments[nextSeg].LaneWidth;
                    if (RoadSegments[nextSeg].IsOneWay == 1)
                    {
                        laneOffset -= (laneCount / 2.0f) * RoadSegments[nextSeg].LaneWidth;
                    }

                    float nextPerct = 0;

                    float3 nextPos = (1.0f - nextPerct) * RoadNodes[RoadSegments[nextSeg].StartNodeId].Position +
                                     (nextPerct) * RoadNodes[RoadSegments[nextSeg].EndNodeId].Position;
                    nextPos += laneOffset * RoadSegments[nextSeg].RightDirection;

                    vehicleData = new VehicleData(
                        vehicleData.Id,
                        nextSeg,
                        vehicleData.Speed,
                        nextForward,
                        nextPerct,
                        1.0f,
                        nextPos,
                        nextLane,
                        50.0f
                    );

                    BVHAABB newAABB = bvhAabb;
                    newAABB.Max += (nextPos - currentPos);
                    newAABB.Min += (nextPos - currentPos);
                    bvhAabb = newAABB;

                    translation = new Translation() { Value = nextPos };
                    rotation = new Rotation() { Value = newRot };
                    return;
                }

                #region Redlight infront
                int currentSeg = vehicleData.SegId;
                int nextCrossing = vehicleData.Direction > 0.0f ? RoadSegments[currentSeg].EndNodeId : RoadSegments[currentSeg].StartNodeId;
                float3 nextCrossingPos = RoadNodes[nextCrossing].Position;
                float distanceToCrossing = math.distance(currentPos, nextCrossingPos);
                int2 nextCrossingGreenlightConnect =
                    RoadNodes[nextCrossing].ConnectionSegIds[RoadNodes[nextCrossing].ActiveConnection];
                if (currentSeg != nextCrossingGreenlightConnect.x && currentSeg != nextCrossingGreenlightConnect.y &&
                    distanceToCrossing < 20.0f)
                {
                    return;
                }
                #endregion

                #region SpeedVariation
                float prevDistanceAhead = vehicleData.HitDistAhead;
                float newDistanceAhead = thisHitResult.FrontHitDistance;
                float distAheadDiff = prevDistanceAhead - newDistanceAhead;
                int hitResult = thisHitResult.HitResultPacked;
                float maxSpeed = RoadSegments[vehicleData.SegId].MaxSpeed;
                float newSpeed = vehicleData.Speed;
                if (hitResult == 0 && newSpeed < maxSpeed)
                {
                    newSpeed += 0.5f;
                }
                else if ((hitResult & 0x1) == 1 && (distAheadDiff > 0))
                {
                    newSpeed -= ((distAheadDiff) / 20.0f);
                }

                if (newDistanceAhead < 5.0f)
                {
                    newSpeed = 0.0f;
                }

                newSpeed = newSpeed > maxSpeed ? maxSpeed : newSpeed;
                newSpeed = newSpeed < 0 ? 0 : newSpeed;
                #endregion

                float stepLength = newSpeed * DeltaTime;
                float segLength = RoadSegments[vehicleData.SegId].Length;
                float stepPerc = stepLength / segLength;
                float nextPosPerc = vehicleData.CurrentSegPos + stepPerc * vehicleData.Direction;


                #region switchLane

                laneCount = RoadSegments[vehicleData.SegId].LaneNumber;
                int currentLane = vehicleData.Lane;
                int laneSwitch = threadRandom.NextInt(-10, 10);
                laneSwitch = laneSwitch / 10;
                if (currentLane == 0 && laneSwitch == -1) laneSwitch = 0;
                if (currentLane == (laneCount - 1) && laneSwitch == 1) laneSwitch = 0;
                nextLane = vehicleData.Lane + laneSwitch;
                #endregion

                //great, still in this seg
                if (nextPosPerc < 1.0f && nextPosPerc > 0.0f)
                {
                    //step forward
                    float3 nextPos = currentPos + currentDir * stepLength;
                    //offset lane
                    nextPos += laneSwitch * vehicleData.Direction * RoadSegments[vehicleData.SegId].LaneWidth *
                               RoadSegments[vehicleData.SegId].RightDirection;
                    vehicleData = new VehicleData(
                        vehicleData.Id,
                        vehicleData.SegId,
                        newSpeed,
                        vehicleData.Forward,
                        nextPosPerc,
                        vehicleData.Direction,
                        nextPos,
                        nextLane,
                        newDistanceAhead
                        );

                    BVHAABB newAABB = bvhAabb;
                    newAABB.Max += currentDir * stepLength;
                    newAABB.Min += currentDir * stepLength;
                    bvhAabb = newAABB;

                    translation = new Translation() { Value = nextPos };
                }
                //reach end node, find next seg
                else
                {

                    //find next available segment
                    int* _availableSeg = (int*)UnsafeUtility.Malloc(
                        5 * sizeof(int), sizeof(int), Allocator.Temp);
                    int availableSegCount = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        int seg1 = RoadNodes[nextCrossing].ConnectionSegIds[k].x;
                        int seg2 = RoadNodes[nextCrossing].ConnectionSegIds[k].y;
                        if (seg1 != -1 && seg1 != currentSeg && //not current seg, and next seg not one-way
                            !(RoadSegments[seg1].EndNodeId == nextCrossing && RoadSegments[seg1].IsOneWay == 1)) _availableSeg[availableSegCount++] = seg1;
                        if (seg2 != -1 && seg2 != currentSeg && //not current seg, and next seg not one-way
                            !(RoadSegments[seg2].EndNodeId == nextCrossing && RoadSegments[seg2].IsOneWay == 1)) _availableSeg[availableSegCount++] = seg2;
                    }

                    int nextSeg = currentSeg;
                    float dir = 1.0f;
                    if (availableSegCount > 0)//luckily we can proceed
                    {
                        int selectSegId = threadRandom.NextInt(0, availableSegCount);
                        nextSeg = _availableSeg[selectSegId];
                        dir = RoadSegments[nextSeg].StartNodeId == nextCrossing ? 1.0f : -1.0f;
                    }
                    else//to the end, spawn a new pos
                    {
                        nextSeg = threadRandom.NextInt(0, RoadSegments.Length);
                    }

                    float3 nextForward = RoadSegments[nextSeg].Direction * dir;
                    quaternion newRot = quaternion.LookRotation(nextForward, new float3() { x = 0, y = 1, z = 0 });

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

                    vehicleData = new VehicleData(
                        vehicleData.Id,
                        nextSeg,
                        newSpeed,
                        nextForward,
                        nextPerct,
                        dir,
                        nextPos,
                        nextLane,
                        newDistanceAhead
                    );

                    BVHAABB newAABB = bvhAabb;
                    newAABB.Max += (nextPos - currentPos);
                    newAABB.Min += (nextPos - currentPos);
                    bvhAabb = newAABB;

                    translation = new Translation() { Value = nextPos };
                    rotation = new Rotation() { Value = newRot };

                    UnsafeUtility.Free(_availableSeg, Allocator.Temp);
                    RdGens[threadId] = threadRandom;
                    _availableSeg = null;
                }
            }
        }

        [BurstCompile]
        struct WriteSenseResultJob : IJobChunk
        {
            [NativeDisableParallelForRestriction]
            [DeallocateOnJobCompletion]
            public NativeArray<HitResult> HitResultTemp;

            public ArchetypeChunkComponentType<HitResult> HitResult;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var hitResultChunk = chunk.GetNativeArray(HitResult);
                for (var i = 0; i < chunk.Count; i++)
                {
                    var globalId = firstEntityIndex + i;
                    hitResultChunk[i] = HitResultTemp[globalId];
                }
            }
        }

        [BurstCompile]
        struct SenseEnvironmentJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<BVHNode> BVHArray;

            [ReadOnly]
            [NativeDisableParallelForRestriction]
            [DeallocateOnJobCompletion]
            public NativeArray<VehicleData> VehicleData;

            [NativeDisableParallelForRestriction]
            public NativeArray<HitResult> HitResult;

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
                        if (right != leafNodeIndex) QueryBVHNode(right, leafNodeIndex, forward, position);
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
        #endregion
    }
}
