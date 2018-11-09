using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using OSMTrafficSim.BVH;

namespace OSMTrafficSim
{
    public class VehicleSystem : JobComponentSystem
    {

        struct VehicleGroup
        {
            public readonly int Length;
            public EntityArray Entity;
            public ComponentDataArray<VehicleData> VehicleData;
            public ComponentDataArray<AABB> AABB;
            public ComponentDataArray<Position> Position;
            public ComponentDataArray<Rotation> Rotation;
            public ComponentDataArray<HitResult> HitResult;
        }
        [Inject] VehicleGroup _vehicleGroup;
        
        private int Capacity = 1024;

        #region ComponentSystem Interface

        protected override void OnCreateManager()
        {
            Capacity = TrafficConfig.Instance.MaxVehicles;

            VehicleFactory.Init(EntityManager);
            for (var j = 0; j < Capacity; j++)
            {
                VehicleFactory.AddVehicle(EntityManager);
            }

            _roadSegments = new NativeArray<RoadSegment>(RoadGraph.Instance.RoadSegments.ToArray(), Allocator.Persistent);
            _roadNodes = new NativeArray<RoadNode>(RoadGraph.Instance.RoadNodes.ToArray(), Allocator.Persistent);
            _roadNodes.CopyFrom(RoadGraph.Instance.RoadNodes.ToArray());
            _roadSegments.CopyFrom(RoadGraph.Instance.RoadSegments.ToArray());

            var seeds = Utils.GetRandomSeed(Capacity);
            _randSeed = new NativeArray<uint>(seeds, Allocator.Persistent);
            _randSeed.CopyFrom(seeds);

            _BVH = new BVHConstructor(Capacity);
        }
        protected override void OnDestroyManager()
        {
            _roadNodes.Dispose();
            _roadSegments.Dispose();
            _randSeed.Dispose();
            _BVH.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            deps = _BVH.Calculate(deps, _vehicleGroup.AABB);

            var senseJob = new SenseEnvironmentJob()
            {
                VehicleData = _vehicleGroup.VehicleData,
                BVHArray = _BVH.BVHArray,
                HitResult = _vehicleGroup.HitResult,
                HalfBVHArrayLength = _BVH.BVHArray.Length / 2,
            };

            deps = senseJob.Schedule(Capacity, 64, deps);

            var vehicleMoveJob = new VehicleMoveJob()
            {
                Positions = _vehicleGroup.Position,
                Rotations = _vehicleGroup.Rotation,
                VehicleData = _vehicleGroup.VehicleData,
                RoadNodes = _roadNodes,
                RoadSegments = _roadSegments,
                RandSeed = _randSeed,
                HitResult = _vehicleGroup.HitResult,
                AABB = _vehicleGroup.AABB,
                FrameSeed = (uint)Time.frameCount,
                DeltaTime = Time.deltaTime
            };
            deps = vehicleMoveJob.Schedule(Capacity, 64, deps);

            return deps;
        }
        #endregion

        private BVHConstructor _BVH;
        private NativeArray<RoadNode> _roadNodes;
        private NativeArray<RoadSegment> _roadSegments;
        private NativeArray<uint> _randSeed;
    }
}
