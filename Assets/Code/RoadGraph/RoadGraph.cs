using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OSMTrafficSim
{

    [System.Serializable]
    public struct RoadNode : IComponentData
    {
        public int NodeId;
        public int2x3 ConnectionSegIds;//max 3 mutex traffic light
        public float3 Position;

        public RoadNode(int id)
        {
            NodeId = id;
            ConnectionSegIds = -1;
            Position = 0;
        }
    }

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
        public int Level;//1 primary, 2 secondary, 3 others
        public float MaxSpeed;
        public int NameHashcode;
    }

    public class RoadGraph : MonoBehaviour
    {
        private static RoadGraph _instance;
        public static RoadGraph Instance {
            get {
                if (_instance == null) {
                    _instance = GameObject.FindObjectOfType<RoadGraph>();
                }
                return _instance;
            }
        }

        public bool bDebug;
        public bool bDeepDebug;
        public Vector2 RefCenter;
        
        public List<RoadSegment> RoadSegments
        {
            get { return roadSegments; }
        }
        public List<RoadNode> RoadNodes
        {
            get { return roadNodes; }
        }
        [HideInInspector]
        [SerializeField]
        private List<RoadSegment> roadSegments;
        [HideInInspector]
        [SerializeField]
        private List<RoadNode> roadNodes;

        public void Awake()
        {
            _instance = this;
        }

        public void Init(GeoJson rawData) {
            List<List<int>> nodeConnects = new List<List<int>>();
            Dictionary<Vector2Int, RoadNode> nodeDic = new Dictionary<Vector2Int, RoadNode>();
            roadNodes = new List<RoadNode>();
            roadSegments = new List<RoadSegment>();
            //iterate over each road
            foreach (var feature in rawData.features)
            {
                List<RoadNode> nodes = new List<RoadNode>();
                HashSet< Vector2Int> uniqueNodes = new HashSet<Vector2Int>();
                int len = feature.geometry.coordinates.Length;
                //add nodes
                for (int i = 0; i < len / 2; i++)
                {
                    Vector2 latlongPos = new Vector2(
                        feature.geometry.coordinates[i, 0],
                        feature.geometry.coordinates[i, 1]);
                    Vector2 worldPos = Conversion.GeoToWorldPosition(latlongPos, RefCenter);
                    Vector2Int intpos = new Vector2Int((int)worldPos.x, (int)worldPos.y);
                    if (!uniqueNodes.Contains(intpos))
                    {
                        uniqueNodes.Add(intpos);
                    }
                    else
                    {
                        continue;
                    }
                    RoadNode node;
                    if (!nodeDic.TryGetValue(intpos, out node))
                    {
                        node = new RoadNode(RoadNodes.Count);
                        node.Position = new float3(){ x = worldPos.x, y = 0, z = worldPos.y};
                        nodeDic.Add(intpos, node);
                        RoadNodes.Add(node);
                        nodeConnects.Add(new List<int>());
                    }
                    /*else
                    {
                        Debug.Log("alreadyexist");
                    }*/
                    nodes.Add(node);
                }
                //add segments
                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    RoadSegment seg = new RoadSegment();
                    seg.StartNodeId = nodes[i].NodeId;
                    seg.EndNodeId = nodes[i + 1].NodeId;
                    float3 dir = nodes[i + 1].Position - nodes[i].Position;
                    seg.Length = Vector3.Magnitude(dir);
                    dir = Vector3.Normalize(dir);
                    seg.Direction = dir;
                    seg.RightDirection = math.cross(dir, new float3(0, 1, 0));
                    seg.IsOneWay = feature.properties.oneway == "yes" ? 1 : 0;
                    seg.LaneNumber = feature.properties.lanes == null ? 1 : int.Parse(feature.properties.lanes);
                    seg.Level = GetRoadLevel(feature.properties.highway);
                    seg.LaneWidth = GetLaneWidth(seg.Level);
                    seg.SegmentId = roadSegments.Count;
                    float2 speedRange = GetSpeedRange(seg.Level);
                    seg.MaxSpeed = speedRange.y / 3.6f;
                    seg.NameHashcode = feature.id == null ? 0 : feature.id.GetHashCode();
                    roadSegments.Add(seg);
                    nodeConnects[nodes[i].NodeId].Add(seg.SegmentId);
                    nodeConnects[nodes[i + 1].NodeId].Add(seg.SegmentId);
                }
            }

            //fill Nodes' ConnectionSegIds and ConnectionGroups field
            for (int i = 0; i < RoadNodes.Count; i++) {
                RoadNode nd = RoadNodes[i];
                //calculate ConnectionGroups field
                int namehash = roadSegments[nodeConnects[i][0]].NameHashcode;
                int count = 0;
                int groupid = 0;
                int2 roadPairSegId = -1;
                foreach (var segId in nodeConnects[i]) {
                    if (roadSegments[segId].NameHashcode == namehash)
                    {
                        if (count == 0) roadPairSegId.x = segId;
                        else roadPairSegId.y = segId;
                        count++;
                    }
                    else
                    {
                        if (groupid < 3) nd.ConnectionSegIds[groupid] = roadPairSegId;
                        roadPairSegId = -1;
                        roadPairSegId.x = segId;
                        count = 1;
                        groupid++;
                        namehash = roadSegments[segId].NameHashcode;
                    }
                }
                if (groupid < 3) nd.ConnectionSegIds[groupid] = roadPairSegId;

                //prevent triple traffic light on crossroad
                if (nd.ConnectionSegIds[1].x != -1 && nd.ConnectionSegIds[1].y == -1 &&
                    nd.ConnectionSegIds[2].x != -1 && nd.ConnectionSegIds[2].y == -1)
                {
                    nd.ConnectionSegIds[1] = new int2(){ x = nd.ConnectionSegIds[1].x , y = nd.ConnectionSegIds[2].x };
                    nd.ConnectionSegIds[2] = -1;
                }

                RoadNodes[i] = nd;
            }
        }

        public void InitRandom()
        {
            RandomGen = new Unity.Mathematics.Random();
           // RandomGen.InitState((uint)System.DateTime.Now.Millisecond);
            RandomGen.InitState();
        }

        public void GetNextRandomPosition(out float3 position, out quaternion rotation, out float3 direction, out float speed, 
            out int currentid, out float lerpPos, out float dir, out int lane)
        {
            currentid = RandomGen.NextInt(0, RoadSegments.Count);
            int fromid = RoadSegments[currentid].StartNodeId;
            int toid = RoadSegments[currentid].EndNodeId;

            float laneWidth = RoadSegments[currentid].LaneWidth;
            int laneCount = RoadSegments[currentid].LaneNumber;
            lane = RandomGen.NextInt(0, laneCount);

            Vector3 roadVec = RoadSegments[currentid].Direction;
            dir = RoadSegments[currentid].IsOneWay == 1 ? 1.0f : (RandomGen.NextInt(0, 2) - 0.5f) * 2.0f;
            roadVec *= dir;
            direction = roadVec.normalized;

            float3 offsetDir = math.cross(direction, new float3() {x = 0, y = 1, z = 0});
            float offset = (lane + 0.5f) * laneWidth;
            if (RoadSegments[currentid].IsOneWay == 1)
            {
                offset -= (laneCount / 2.0f) * laneWidth;
            }
            lerpPos = RandomGen.NextFloat(0, 1);
            position = RoadNodes[toid].Position * lerpPos +
                       RoadNodes[fromid].Position * (1.0f - lerpPos);
            position += offsetDir * offset;

            Quaternion facing = Quaternion.LookRotation(roadVec, Vector3.up);
            rotation = facing;

            float2 speedRange = GetSpeedRange(RoadSegments[currentid].Level);
            speed = RandomGen.NextFloat(speedRange.x, speedRange.y);
            speed /= 3.6f;//km/h to m/s;
        }
#if UNITY_EDITOR
        public ComponentDataArray<VehicleData> VehicleData;
        public ComponentDataArray<HitResult> hit;
        public void OnDrawGizmos()
        {
            if (!bDebug) return;
            /*Gizmos.color = Color.red;
            foreach (var node in RoadNodes) {
                Gizmos.DrawWireSphere(node.Position, 3.0f);
                Handles.color = Color.red;
                if(bDeepDebug) Handles.Label(node.Position,node.NodeId.ToString());
            }
            foreach (var seg in RoadSegments)
            {
                Vector3 pos1 = RoadNodes[seg.StartNodeId].Position;
                Vector3 pos2 = RoadNodes[seg.EndNodeId].Position;
                Gizmos.color = seg.IsOneWay == 1 ? Color.cyan : Color.magenta;
                Gizmos.DrawLine(pos1, pos2);
                Vector3 mid = (pos1 + pos2) / 2.0f;
                Handles.color = Color.cyan;
                if (bDeepDebug) Handles.Label(mid, string.Format("num{0}, speed{1}",
                    seg.SegmentId,seg.MaxSpeed
                    ));
            }*/
            /*Gizmos.color = Color.magenta;
            for (int i = 0; i < VehicleData.Length; i++)
            {
                Handles.Label(VehicleData[i].Position, string.Format("HitDist{0}",
                    hit[i].FrontHitDistance
                    ));
                Gizmos.DrawRay(VehicleData[i].Position, VehicleData[i].Forward * 10);
            }*/
        }

        [MenuItem("OSMTrafficSim/GenPointLights")]
        private static void GenLights()
        {
            float div = 50.0f;
            GameObject light = new GameObject("light");
            Light lt = light.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.lightmapBakeType = LightmapBakeType.Baked;
            lt.range = 20.0f;
            lt.intensity = 5.0f;
            foreach (var seg in Instance.RoadSegments)
            {
                if (seg.Level == 3) continue;
                float roadlen = seg.Length;
                int lightcount =(int)( roadlen / div);
                if (lightcount > 0)
                {
                    Vector3 direction = Vector3.Normalize(seg.Direction);
                    Vector3 start = Instance.RoadNodes[seg.StartNodeId].Position;
                    for (int i = 0; i < lightcount; i++)
                    {
                        float dist = (i + 0.5f) * div;
                        Vector3 newpos = start + direction * dist;
                        newpos.y = 10.0f;
                        Instantiate(light, newpos, Quaternion.identity);
                    }

                }
            }
        }
        
#endif

        private int GetRoadLevel(string typename)
        {
            switch (typename)
            {
                case ("primary"):
                    return 1;
                case ("secondary"):
                    return 2;
                default:
                    return 3;
            }
        }

        private float GetLaneWidth(int level)
        {
            switch (level)
            {
                case (1):
                    return 4;
                case (2):
                    return 3.75f;
                default:
                    return 3.5f;
            }
        }

        private float2 GetSpeedRange(int roadLevel)
        {
            switch (roadLevel)
            {
                case (1):
                    return new float2(){x = 60, y = 80};
                case (2):
                    return new float2() { x = 40, y = 60 };
                default:
                    return new float2() { x = 20, y = 40 };
            }
        }

        [HideInInspector]
        public Unity.Mathematics.Random RandomGen;

    }
}
