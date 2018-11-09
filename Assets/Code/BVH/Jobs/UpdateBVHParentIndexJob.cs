using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace OSMTrafficSim.BVH
{

    [BurstCompile]
    struct UpdateBVHParentIndexJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ParentIndex;

        public NativeArray<BVHNode> BVHArray;

        public void Execute(int i)
        {
            BVHNode node = BVHArray[i];
            node.ParentNodeIndex = i == 0 ? -1 : ParentIndex[i];
            BVHArray[i] = node;
        }
    }
}