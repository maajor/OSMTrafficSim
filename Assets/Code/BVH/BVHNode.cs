using Unity.Mathematics;

namespace OSMTrafficSim.BVH
{
    public struct BVHNode
    {
        public BVHAABB aabb;
        public int EntityId;
        public int LeftNodeIndex;
        public int RightNodeIndex;
        public int ParentNodeIndex;
        public byte IsValid;
    }
}