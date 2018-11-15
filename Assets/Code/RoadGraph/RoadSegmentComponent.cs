using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace OSMTrafficSim
{

    [System.Serializable]
    public struct RoadSegment : IComponentData
    {
        public int SegmentId;
        public int StartNodeId;
        public int EndNodeId;
        public float3 Direction;
        public float3 RightDirection;
        public float Length;
        public float LaneWidth;
        public int LaneNumber;
        public int IsOneWay;
        public int Level; //1 primary, 2 secondary, 3 others
        public float MaxSpeed;
        public int NameHashcode;
    }

    public class RoadSegmentComponent : ComponentDataWrapper<RoadNode>
    {
    }

    public struct RoadSegmentGroup
    {
        public ComponentDataArray<RoadSegment> RoadSegments;
    }
}