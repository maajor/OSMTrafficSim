using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using OSMTrafficSim.BVH;
using Unity.Mathematics;
using Unity.Rendering;

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
        [Inject] RoadSegmentGroup _roadSegmentGroup;
        [Inject] RoadNodeGroup _roadNodeGroup;

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

            var seeds = Utils.GetRandomSeed(Capacity);
            _randSeed = new NativeArray<uint>(seeds, Allocator.Persistent);
            _randSeed.CopyFrom(seeds);
            _bound = RoadGraph.Instance.BoundingBox;

            _BVH = new BVHConstructor(Capacity);
        }
        protected override void OnDestroyManager()
        {
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
                RoadNodes = _roadNodeGroup.RoadNodes,
                RoadSegments = _roadSegmentGroup.RoadSegments,
                HitResult = _vehicleGroup.HitResult,
                AABB = _vehicleGroup.AABB,
                FrameSeed = (uint)Time.frameCount,
                DeltaTime = Time.deltaTime,
                BoundingBox = _bound,
                rdGen = new Unity.Mathematics.Random(_randSeed[Time.frameCount % _randSeed.Length])
            };
            deps = vehicleMoveJob.Schedule(Capacity, 64, deps);

            return deps;
        }
        #endregion

        private BVHConstructor _BVH;
        private NativeArray<uint> _randSeed;
        private Bounds _bound;
    }
}
