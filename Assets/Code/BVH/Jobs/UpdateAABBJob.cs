using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace OSMTrafficSim.BVH
{
    [BurstCompile]
    unsafe struct UpdateAABBJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<BVHNode> BVHArray;

        [NativeDisableUnsafePtrRestriction]
        public long* locks;

        public void Execute(int i)
        {

            int halfLength = BVHArray.Length / 2;
            int leafNodeId = halfLength + i;
            BVHAABB leafNodeAABB = BVHArray[leafNodeId].aabb;
            int parentIndex = BVHArray[leafNodeId].ParentNodeIndex;
            while (parentIndex != -1)
            {
                //todo locks!
                BVHNode parent = BVHArray[parentIndex];
                if (parent.IsValid < 1)
                {
                    parent.aabb = leafNodeAABB;
                    parent.IsValid = 1;
                    BVHArray[parentIndex] = parent;
                    break;
                }
                else
                {
                    parent.aabb = Utils.GetEncompassingAABB(parent.aabb, leafNodeAABB);
                    parent.IsValid = 2;
                    BVHArray[parentIndex] = parent;
                }
                leafNodeAABB = parent.aabb;
                parentIndex = parent.ParentNodeIndex;
            }

        }

        public void SerialExecute(int i)
        {
            int halfLength = BVHArray.Length / 2;
            int leafNodeId = halfLength + i;
            BVHAABB leafNodeAABB = BVHArray[leafNodeId].aabb;
            int parentIndex = BVHArray[leafNodeId].ParentNodeIndex;
            while (parentIndex != -1)
            {
                BVHNode parent = BVHArray[parentIndex];
                if (parent.IsValid == 0)
                {
                    parent.aabb = leafNodeAABB;
                    parent.IsValid = 1;
                    BVHArray[parentIndex] = parent;
                }
                parent.aabb = Utils.GetEncompassingAABB(parent.aabb, leafNodeAABB);
                parent.IsValid = 2;
                BVHArray[parentIndex] = parent;
                leafNodeAABB = parent.aabb;
                parentIndex = parent.ParentNodeIndex;
            }

        }
    }
}