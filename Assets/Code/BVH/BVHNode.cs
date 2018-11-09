namespace OSMTrafficSim.BVH
{
    public struct BVHNode
    {
        public AABB aabb;
        public int EntityId;
        public int LeftNodeIndex;
        public int RightNodeIndex;
        public int ParentNodeIndex;
        public byte IsValid;
    }
}