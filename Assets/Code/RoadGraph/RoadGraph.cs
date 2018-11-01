using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Unity.Collections;

namespace OSMTrafficSim
{

    [System.Serializable]
    public struct RoadNode : IComponentData
    {
        public int NodeId;
        public int[] ConnectionSegsIds;
        public int[] ConnectionGroups;
        public Vector2 Position;
    }

    [System.Serializable]
    public struct RoadSegment : IComponentData
    {
        public int SegmentId;
        public int[] EndNodeIds;
        public float LaneWidth;
        public int LaneNumber;
        public bool IsOneWay;
        public string Name;
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

        public Vector2 RefCenter;
        public List<RoadNode> RoadNodesTemp;
        public List<RoadSegment> RoadSegmentsTemp;

        public void Awake()
        {
            _instance = this;
        }

        public void Init(GeoJson rawData) {
            List<List<int>> nodeConnects = new List<List<int>>();
            Dictionary<Vector2Int, RoadNode> nodeDic = new Dictionary<Vector2Int, RoadNode>();
            RoadNodesTemp = new List<RoadNode>();
            RoadSegmentsTemp = new List<RoadSegment>();
            foreach (var feature in rawData.features)
            {
                List<RoadNode> nodes = new List<RoadNode>();
                int len = feature.geometry.coordinates.Length;
                for (int i = 0; i < len / 2; i++)
                {
                    Vector2 latlongPos = new Vector2(
                        feature.geometry.coordinates[i, 0],
                        feature.geometry.coordinates[i, 1]);
                    Vector2 worldPos = Conversion.GeoToWorldPosition(latlongPos, RefCenter);
                    Vector2Int intpos = new Vector2Int((int)worldPos.x, (int)worldPos.y);
                    RoadNode node;
                    if (!nodeDic.TryGetValue(intpos, out node))
                    {
                        node = new RoadNode();
                        node.NodeId = RoadNodesTemp.Count;
                        node.Position = worldPos;
                        nodeDic.Add(intpos, node);
                        RoadNodesTemp.Add(node);
                        nodeConnects.Add(new List<int>());
                    }
                    else
                    {
                        Debug.Log("alreadyexist");
                    }

                    nodes.Add(node);
                }
                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    RoadSegment seg = new RoadSegment();

                    seg.EndNodeIds = new int[2] { nodes[i].NodeId, nodes[i + 1].NodeId};
                    seg.IsOneWay = feature.properties.oneway == "yes";
                    seg.LaneWidth = 3.5f;
                    seg.LaneNumber = 2;
                    seg.SegmentId = RoadSegmentsTemp.Count;
                    RoadSegmentsTemp.Add(seg);
                    nodeConnects[nodes[i].NodeId].Add(seg.SegmentId);
                    nodeConnects[nodes[i + 1].NodeId].Add(seg.SegmentId);
                }
            }

            for (int i = 0; i < RoadNodesTemp.Count; i++) {
                RoadNode nd = RoadNodesTemp[i];
                nd.ConnectionSegsIds = nodeConnects[i].ToArray();
                RoadNodesTemp[i] = nd;
            }
            
            //RoadNodes.CopyFrom(RoadNodesTemp.ToArray());
            //RoadSegments.CopyFrom(RoadSegmentsTemp.ToArray());
        }
        //private NativeArray<RoadNode> RoadNodes;
        //private NativeArray<RoadSegment> RoadSegments;

        public void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            foreach (var node in RoadNodesTemp) {
                Gizmos.DrawWireSphere(
                    new Vector3(
                        node.Position.x,
                        0,
                        node.Position.y
                        ), 1.0f);
            }
            foreach (var segs in RoadSegmentsTemp)
            {
                Vector3 pos1 = new Vector3(
                        RoadNodesTemp[segs.EndNodeIds[0]].Position.x,
                        0,
                         RoadNodesTemp[segs.EndNodeIds[0]].Position.y
                        );
                Vector3 pos2 = new Vector3(
                        RoadNodesTemp[segs.EndNodeIds[1]].Position.x,
                        0,
                         RoadNodesTemp[segs.EndNodeIds[1]].Position.y
                        );
                Gizmos.DrawLine(
                    pos1, pos2);
            }
        }

    }
}
