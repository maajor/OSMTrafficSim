using System;
using Unity.Entities;
using Unity.Mathematics;

namespace OSMTrafficSim
{
    [Serializable]
    public struct VehicleData : IComponentData
    {
        public uint Id;
        public int SegId;
        public float Speed;
        public float3 Forward;
        public float Direction;//-1.0f for backward of roadDir, 1.0f for forward of roadDir
        public float CurrentSegPos;
        public float3 Position;
        public int Lane;
        public float HitDistAhead;

        public VehicleData(uint id, int currentid, float speed, float3 forward, float segpos, float dir, float3 position, int lane, float hitDistAhead)
        {
            this.Id = id;
            this.SegId = currentid;
            this.Speed = speed;
            this.Forward = forward;
            this.CurrentSegPos = segpos;
            this.Direction = dir;
            this.Position = position;
            this.Lane = lane;
            this.HitDistAhead = hitDistAhead;
        }
    }
    
    public class VehicleComponent : ComponentDataProxy<VehicleData> {

        public VehicleComponent(uint id, int currentid, float speed, float3 direction, float segpos, float dir, float3 position, int lane, float hitDistAhead) {
            Value = new VehicleData(id, currentid, speed, direction, segpos, dir, position, lane, hitDistAhead);
        }
        
    }
}
