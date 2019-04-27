using System;
using Unity.Entities;

namespace OSMTrafficSim
{
    [Serializable]
    public struct HitResult : IComponentData
    {
        public float FrontHitDistance;
        public int HitResultPacked;//0x1 front, 0x2 left, 0x4 right
        public int FrontEntityId;
    }
    public class HitResultComponent : ComponentDataProxy<HitResult>{}
}