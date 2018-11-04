using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrafficConfig : MonoBehaviour {

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
}
