using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using OSMTrafficSim.BVH;

namespace OSMTrafficSim
{
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
}