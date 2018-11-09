using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace OSMTrafficSim.BVH
{
    [BurstCompile]
    struct ResetBVH : IJobParallelFor
    {
        public NativeArray<BVHNode> BVHArray;

        public void Execute(int i)
        {
            BVHNode bvhNode = BVHArray[i];
            bvhNode.IsValid = 0;
            bvhNode.ParentNodeIndex = -1;
            bvhNode.aabb = new AABB();
            BVHArray[i] = bvhNode;
        }
    }
}