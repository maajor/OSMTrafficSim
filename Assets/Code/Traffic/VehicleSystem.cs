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
        private ComponentGroup _vehicleGroup;
        private ComponentGroup _roadSegmentGroup;
        private ComponentGroup _roadNodeGroup;

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

            _vehicleGroup = GetComponentGroup(typeof(VehicleData), typeof(BVHAABB), typeof(Position), typeof(Rotation), typeof(HitResult));
            _roadSegmentGroup = GetComponentGroup(typeof(RoadSegment));
            _roadNodeGroup = GetComponentGroup(typeof(RoadNode));
        }
        protected override void OnDestroyManager()
        {
            _randSeed.Dispose();
            _BVH.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            var vehicleAABB = _vehicleGroup.GetComponentDataArray<BVHAABB>();
            var vehicleData = _vehicleGroup.GetComponentDataArray<VehicleData>();
            var vehicleHitresult = _vehicleGroup.GetComponentDataArray<HitResult>();

            deps = _BVH.Calculate(deps, vehicleAABB);

            var senseJob = new SenseEnvironmentJob()
            {
                VehicleData = vehicleData,
                BVHArray = _BVH.BVHArray,
                HitResult = vehicleHitresult,
                HalfBVHArrayLength = _BVH.BVHArray.Length / 2,
            };

            deps = senseJob.Schedule(Capacity, 64, deps);

            var vehicleMoveJob = new VehicleMoveJob()
            {
                Positions = _vehicleGroup.GetComponentDataArray<Position>(),
                Rotations = _vehicleGroup.GetComponentDataArray<Rotation>(),
                VehicleData = vehicleData,
                RoadNodes = _roadNodeGroup.GetComponentDataArray<RoadNode>(),
                RoadSegments = _roadSegmentGroup.GetComponentDataArray<RoadSegment>(),
                HitResult = vehicleHitresult,
                AABB = vehicleAABB,
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
