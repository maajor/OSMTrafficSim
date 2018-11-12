using System;
using System.Collections.Generic;
using UnityEngine;

namespace OSMTrafficSim
{
    [Serializable]
    public struct VehicleTemplate
    {
        public GameObject Prefab;
        public float Weight;
    }

    public class TrafficConfig : MonoBehaviour
    {

        private static TrafficConfig _instance;

        public static TrafficConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GameObject.FindObjectOfType<TrafficConfig>();
                }

                return _instance;
            }
        }

        public void Awake()
        {
            _instance = this;
        }

        public int MaxVehicles = 1024;
        public List<VehicleTemplate> Templates;
        public int MaxPedestrian = 1024;
        public Mesh ManMesh;
        public Material ManMat;
        public float PedestrianCullDistance;

        public void OnValidate()
        {
            List< VehicleTemplate > valid = new List<VehicleTemplate>();
            foreach (var veh in Templates)
            {
                if (veh.Prefab == null)
                {
                    valid.Add(veh);
                    continue;
                }
                var mf = veh.Prefab.GetComponent<MeshFilter>();
                if (mf == null) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var mr = veh.Prefab.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var mat = mr.sharedMaterial;
                if (mat == null) continue;
                valid.Add(veh);
            }
            Templates = valid;
        }
    }
}
