using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using OSMTrafficSim.BVH;

namespace OSMTrafficSim
{
    [BurstCompile]
    struct VehicleMoveJob : IJobParallelFor
    {
        public float DeltaTime;
        public uint FrameSeed;

        [ReadOnly]
        public ComponentDataArray<RoadNode> RoadNodes;

        [ReadOnly]
        public ComponentDataArray<RoadSegment> RoadSegments;

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

            #region Redlight infront
            int currentSeg = VehicleData[i].SegId;
            int nextCrossing = VehicleData[i].Direction > 0.0f ? RoadSegments[currentSeg].EndNodeId : RoadSegments[currentSeg].StartNodeId;
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
            float prevDistanceAhead = VehicleData[i].HitDistAhead;
            float newDistanceAhead = HitResult[i].FrontHitDistance;
            float distAheadDiff = prevDistanceAhead - newDistanceAhead;
            int hitResult = HitResult[i].HitResultPacked;
            float maxSpeed = RoadSegments[VehicleData[i].SegId].MaxSpeed;
            float newSpeed = VehicleData[i].Speed;
            if (hitResult == 0 && newSpeed < maxSpeed)
            {
                newSpeed += 0.5f;
            }
            else if ((hitResult & 0x1) == 1 && (distAheadDiff > 0))
            {
                newSpeed -= ((distAheadDiff ) / 20.0f);
            }

            if (newDistanceAhead < 5.0f)
            {
                newSpeed = 0.0f;
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
                //int reachedNode = VehicleData[i].Direction > 0.0f ? RoadSegments[currentSeg].EndNodeId : RoadSegments[currentSeg].StartNodeId;
                
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
                    int selectSegId = rdGen.NextInt(0, availableSegCount);
                    nextSeg = _availableSeg[selectSegId];
                    dir = RoadSegments[nextSeg].StartNodeId == nextCrossing ? 1.0f : -1.0f;
                }
                else//to the end, spawn a new pos
                {
                    nextSeg = rdGen.NextInt(0, RoadSegments.Length);
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
}