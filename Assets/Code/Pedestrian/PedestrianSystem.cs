using System.Runtime.CompilerServices;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;
using WalkablePatch = System.UInt64;

namespace OSMTrafficSim
{

    public class PedestrianSystem : JobComponentSystem
    {
        private int _capacity = 1024;

        private float2 texelSize;
        private int patchResolution;

        protected override void OnCreateManager()
        {
            _capacity = TrafficConfig.Instance.MaxPedestrian;

            PedestrianFactory.Init(EntityManager);
            for (var j = 0; j < _capacity; j++)
            {
                PedestrianFactory.AddPedestrian(EntityManager);
            }

            _walkableArea = new NativeArray<ulong>(PedestrianArea.Instance.WalkableArea.Count, Allocator.Persistent);
            _walkableArea.CopyFrom(PedestrianArea.Instance.WalkableArea.ToArray());

            var seeds = Utils.GetRandomSeed(_capacity);
            _randSeed = new NativeArray<uint>(seeds, Allocator.Persistent);
            _randSeed.CopyFrom(seeds);

            Vector3 texel = PedestrianArea.Instance.Size / (PedestrianArea.Instance.PatchResolution * 8);
            texelSize = new float2(texel.x, texel.z);
            patchResolution = PedestrianArea.Instance.PatchResolution;

            _pedestrianAnimStateConfig = TrafficConfig.Instance.PedestrianConfig.State;

        }
        protected override void OnDestroyManager()
        {
            _walkableArea.Dispose();
            _randSeed.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            var senseJob = new PedestrianMoveCheckJob()
            {
                WalkableArea = _walkableArea,
                TexelSize = texelSize,
                PatchResolution = patchResolution,
                DeltaTime = Time.deltaTime,
                RdGen = new Random(_randSeed[(Time.frameCount % _capacity)])
            };

            deps = senseJob.Schedule(this, deps);
            
            var stateJob = new PedestrianStateTransitionJob()
            {
                RdGen = new Random(_randSeed[(Time.frameCount % _capacity)]),
                DeltaTime = Time.deltaTime,
                PedestrianAnimStateConfig = _pedestrianAnimStateConfig
            };
            deps = stateJob.Schedule(this, deps);

            var moveJob = new PedestrianMoveJob();
            deps = moveJob.Schedule(this, deps);

            return deps;
        }

        [BurstCompile]
        struct PedestrianMoveCheckJob : IJobProcessComponentData<PedestrianData>
        {
            public float DeltaTime;

            [ReadOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<ulong> WalkableArea;

            public float2 TexelSize;
            public int PatchResolution;
            public Random RdGen;

            public void Execute(ref PedestrianData thisPed)
            {
                float rotateAngle = RdGen.NextFloat(-0.01f, 0.01f);
                float cos = math.cos(rotateAngle);
                float sin = math.sin(rotateAngle);
                float3 newDir = new float3(){ x = thisPed.Forword.x * cos - thisPed.Forword.z * sin, y = 0, z = thisPed.Forword.x * sin + thisPed.Forword.z * cos};
                float3 step = newDir * thisPed.Speed * DeltaTime;
                float2 newLocalPos = step.xz * math.rcp(TexelSize) + thisPed.LocalPos;
                int2 GridIdAdd = (int2)math.floor(newLocalPos);
                bool StillInCurrentTexel = GridIdAdd.x == 0 && GridIdAdd.y == 0;
                int2 newGridId = GridIdAdd + thisPed.GridId;
                float3 newWorldPos = thisPed.WorldPos;
                if (StillInCurrentTexel || IsWalkable(newGridId))//go ahead!
                {
                    newLocalPos -= GridIdAdd;
                    newWorldPos += step;
                }
                else //turn toward gradient!
                {
                    newDir = Gradient(thisPed.GridId);
                    newLocalPos = thisPed.LocalPos;
                }
                thisPed.Forword = newDir;
                thisPed.WorldPos = newWorldPos;
                thisPed.LocalPos = newLocalPos;
                thisPed.GridId = newGridId;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float3 Gradient(int2 gridId)
            {
                float3 result = 0;
                result += IsWalkable(gridId + new int2(1, 0)) ? new float3(1,0, 0) : 0;
                result += IsWalkable(gridId + new int2(0, 1)) ? new float3(0,0, 1) : 0;
                result += IsWalkable(gridId + new int2(-1, 0)) ? new float3(-1,0, 0) : 0;
                result += IsWalkable(gridId + new int2(0, -1)) ? new float3(0, 0,-1) : 0;
                result /= 4.0f;
                return math.normalizesafe(result, new float3(1,0,0));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool IsWalkable(int2 gridId)
            {
                if (gridId.x < 0 || gridId.y < 0 || gridId.x >= PatchResolution * 8 || gridId.y >= PatchResolution * 8) return false;
                int2 patchId = gridId / 8;
                int2 patchLocalId = gridId - patchId * 8;
                return 1ul == ((WalkableArea[patchId.x * PatchResolution + patchId.y] >> (patchLocalId.x * 8 + patchLocalId.y)) & 1ul);
            }

        }

        [BurstCompile]
        struct PedestrianStateTransitionJob : IJobProcessComponentData<PedestrianData, PedestrianState, InstanceRendererProperty>
        {
            public Random RdGen;
            public float DeltaTime;
            public PedestrianAnimStateConfig PedestrianAnimStateConfig;

            public void Execute(ref PedestrianData data, ref PedestrianState state, ref InstanceRendererProperty property)
            {
                int currentState = state.State;
                float cd = state.CoolDown - DeltaTime;
                if (cd < 0)
                {
                    int nextstate = NextState(currentState);

                    property.Value = PedestrianAnimStateConfig.StateFrameRange[nextstate];
                    state.CoolDown = RdGen.NextFloat(
                        PedestrianAnimStateConfig.DurationRange[nextstate].x,
                        PedestrianAnimStateConfig.DurationRange[nextstate].y);
                    data.Speed = RdGen.NextFloat(
                        PedestrianAnimStateConfig.SpeedRange[nextstate].x,
                        PedestrianAnimStateConfig.SpeedRange[nextstate].y);
                }
                else
                {
                    state = new PedestrianState() {CoolDown = cd, State = currentState};
                }
            }

            //markov chain, sample from transition probability matrix
            int NextState(int currentState)
            {
                int nextstate = 3;
                float3 transitionPoss = PedestrianAnimStateConfig.TransitionProbability[currentState];

                float randseed = RdGen.NextFloat();
                if (randseed < transitionPoss.x)
                {
                    nextstate = 0;
                }
                else if (randseed < transitionPoss.x + transitionPoss.y)
                {
                    nextstate = 1;
                }
                else if (randseed < transitionPoss.x + transitionPoss.y + transitionPoss.z)
                {
                    nextstate = 2;
                }

                return nextstate;
            }
        }

        [BurstCompile]
        struct PedestrianMoveJob : IJobProcessComponentData<Position,Rotation,PedestrianData>
        {
            public void Execute([WriteOnly] ref Position position, [WriteOnly] ref Rotation rotation, [ReadOnly] ref PedestrianData data)
            {
                position = new Position() {Value = data.WorldPos};
                rotation = new Rotation() { Value = quaternion.LookRotation(data.Forword, new float3(0,1,0))};
            }
        }

        private NativeArray<WalkablePatch> _walkableArea;
        private NativeArray<uint> _randSeed;
        private PedestrianAnimStateConfig _pedestrianAnimStateConfig;
    }
}
