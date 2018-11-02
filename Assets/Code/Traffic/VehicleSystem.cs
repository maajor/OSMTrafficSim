using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace OSMTrafficSim
{
    public class VehicleSystem : JobComponentSystem
    {
        ComponentGroup _template;
        EntityArchetype _carArchitype;

        private ComponentDataFromEntity<Position> position;
        private ComponentDataFromEntity<Rotation> rotation;

        protected override void OnCreateManager()
        {
            Debug.Log("Create Traffic System");
        }

        protected override void OnStartRunning() {

            Unity.Mathematics.Random randomGen = new Unity.Mathematics.Random();
            randomGen.InitState();
            
            _template = GetComponentGroup(typeof(VehicleTemplate));
            var iterator = _template.GetEntityArray();
            var entities = new NativeArray<Entity>(iterator.Length, Allocator.Temp);
            iterator.CopyTo(entities);

            _carArchitype = EntityManager.CreateArchetype(
                typeof(VehicleData), typeof(Position), typeof(Rotation), typeof(MeshInstanceRenderer)
            );

            for (var j = 0; j < 1024; j++)
            {
                var car = EntityManager.CreateEntity(_carArchitype);
                EntityManager.SetComponentData(car, new VehicleData((uint)j));
                Position p = new Position();
                p.Value = randomGen.NextFloat3(0, 100);
                p.Value.y = 0;
                EntityManager.SetComponentData(car, p);
                Rotation r = new Rotation();
                r.Value = randomGen.NextQuaternionRotation();
                EntityManager.SetComponentData(car, r);
                int rendererid = randomGen.NextInt(0, entities.Length);
                VehicleTemplate template = EntityManager.GetSharedComponentData<VehicleTemplate>(entities[rendererid]);
                EntityManager.SetSharedComponentData(car, template.renderer);
            }
            entities.Dispose();
        }

        [BurstCompile]
        struct VehicleMoveJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataFromEntity<Position> position;
            [NativeDisableParallelForRestriction]
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataFromEntity<Rotation> rotation;

            public void Execute(int i)
            {

            }
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            var vehicleMoveJob = new VehicleMoveJob
            {
            };
            deps = vehicleMoveJob.Schedule(1024, 64, deps);

            return deps;
        }
    }
}
