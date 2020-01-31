using System.Runtime.CompilerServices;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        #region Component System Interface
        protected override void OnCreate()
        {
            _capacity = TrafficConfig.Instance.MaxPedestrian;

            PedestrianFactory.Init(EntityManager);
            for (var j = 0; j < _capacity; j++)
            {
                PedestrianFactory.AddPedestrian(EntityManager);
            }

            _walkableArea = new NativeArray<ulong>(PedestrianArea.Instance.WalkableArea.Count, Allocator.Persistent);
            _walkableArea.CopyFrom(PedestrianArea.Instance.WalkableArea.ToArray());

            Vector3 texel = PedestrianArea.Instance.Size / (PedestrianArea.Instance.PatchResolution * 8);
            texelSize = new float2(texel.x, texel.z);
            patchResolution = PedestrianArea.Instance.PatchResolution;

            _rdGens = Utils.GetRandomizerPerThread();

            _pedestrianAnimStateConfig = TrafficConfig.Instance.PedestrianConfig.State;

        }
        protected override void OnDestroy()
        {
            _walkableArea.Dispose();
            _rdGens.Dispose();
        }
        protected override JobHandle OnUpdate(JobHandle deps)
        {
            var senseJob = new PedestrianMoveCheckJob()
            {
                WalkableArea = _walkableArea,
                TexelSize = texelSize,
                PatchResolution = patchResolution,
                DeltaTime = Time.DeltaTime,
                RdGens = _rdGens
            };

            deps = senseJob.Schedule(this, deps);
            
            var stateJob = new PedestrianStateTransitionJob()
            {
                RdGens = _rdGens,
                DeltaTime = Time.DeltaTime,
                PedestrianAnimStateConfig = _pedestrianAnimStateConfig
            };
            deps = stateJob.Schedule(this, deps);

            var moveJob = new PedestrianMoveJob();
            deps = moveJob.Schedule(this,deps);
            
            return deps;
        }
        #endregion

        #region Private Fields
        private float2 texelSize;
        private int patchResolution;
        private NativeArray<Unity.Mathematics.Random> _rdGens;
        private NativeArray<WalkablePatch> _walkableArea;
        private PedestrianAnimStateConfig _pedestrianAnimStateConfig;
        #endregion

        #region Jobs In This System
        [BurstCompile]
        struct PedestrianMoveCheckJob : IJobForEach<PedestrianData>
        {
            public float DeltaTime;

            [ReadOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<ulong> WalkableArea;

            public float2 TexelSize;
            public int PatchResolution;

            #pragma warning disable CS0649
            [NativeSetThreadIndex]
            int threadId;

            [NativeDisableParallelForRestriction]
            public NativeArray<Unity.Mathematics.Random> RdGens;

            public void Execute(ref PedestrianData thisPed)
            {
                var threadRandom = RdGens[threadId];
                float rotateAngle = threadRandom.NextFloat(-0.01f, 0.01f);
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
                RdGens[threadId] = threadRandom;
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
        struct PedestrianStateTransitionJob : IJobForEach<PedestrianData, PedestrianState, InstanceRendererProperty>
        {
            public float DeltaTime;
            public PedestrianAnimStateConfig PedestrianAnimStateConfig;

            #pragma warning disable CS0649 
            [NativeSetThreadIndex]
            int threadId;

            [NativeDisableParallelForRestriction]
            public NativeArray<Unity.Mathematics.Random> RdGens;

            public void Execute(ref PedestrianData data, ref PedestrianState state, ref InstanceRendererProperty property)
            {
                var threadRandom = RdGens[threadId];
                int currentState = state.State;
                float cd = state.CoolDown - DeltaTime;
                if (cd < 0)
                {
                    int nextstate = NextState(currentState, threadRandom);

                    property.Value = PedestrianAnimStateConfig.StateFrameRange[nextstate];
                    state.CoolDown = threadRandom.NextFloat(
                        PedestrianAnimStateConfig.DurationRange[nextstate].x,
                        PedestrianAnimStateConfig.DurationRange[nextstate].y);
                    data.Speed = threadRandom.NextFloat(
                        PedestrianAnimStateConfig.SpeedRange[nextstate].x,
                        PedestrianAnimStateConfig.SpeedRange[nextstate].y);
                }
                else
                {
                    state = new PedestrianState() {CoolDown = cd, State = currentState};
                }
                RdGens[threadId] = threadRandom;
            }

            //markov chain, sample from transition probability matrix
            int NextState(int currentState, Random randGen)
            {
                int nextstate = 3;
                float3 transitionPoss = PedestrianAnimStateConfig.TransitionProbability[currentState];

                float randseed = randGen.NextFloat();
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
        struct PedestrianMoveJob : IJobForEach<Translation,Rotation,PedestrianData>
        {
            public void Execute([WriteOnly] ref Translation position, [WriteOnly] ref Rotation rotation, [ReadOnly] ref PedestrianData data)
            {
                position = new Translation() {Value = data.WorldPos};
                rotation = new Rotation() { Value = quaternion.LookRotation(data.Forword, new float3(0,1,0))};
            }
        }
        #endregion
    }
}
