using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace OSMTrafficSim
{

    [System.Serializable]
    public struct RoadNode : IComponentData
    {
        public int NodeId;
        public int2x3 ConnectionSegIds; //max 3 mutex traffic light
        public float3 Position;
        public float CountDown;
        public int ActiveConnection;

        public RoadNode(int id)
        {
            NodeId = id;
            ConnectionSegIds = -1;
            Position = 0;
            CountDown = 0;
            ActiveConnection = 0;
        }
    }

    public class RoadNodeComponent : ComponentDataWrapper<RoadNode>
    {
    }

    public struct RoadNodeGroup
    {
        public ComponentDataArray<RoadNode> RoadNodes;
    }
}