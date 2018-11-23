using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace OSMTrafficSim
{
    [CreateAssetMenu(fileName = "PedestrianConfig", menuName = "OSMTrafficSim/PedestrianConfig", order = 1)]
    public class PedestrianConfig : ScriptableObject
    {

        public Mesh ManMesh;
        public Material ManMat;
        public float PedestrianCullDistance;

        public PedestrianAnimStateConfig State;
    }
    
    //FixedArray not supported yet, maximum 4 state
    [Serializable]
    public struct PedestrianAnimStateConfig
    {
        public int StateCount;
        public float4x4 StateFrameRange;
        public float2x4 DurationRange;
        public float2x4 SpeedRange;
        public float3x4 TransitionProbability;//since all column sum is 1, TransitionPossibilityMatrix
        
    }
}
