using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEditor.Experimental.UIElements.GraphView;
using UnityEditor.VersionControl;
using UnityEngine;
using Random = Unity.Mathematics.Random;
using WalkablePatch = System.UInt64;

namespace OSMTrafficSim
{

    public class PedestrianSystem : JobComponentSystem
    {
        struct PedestrianGroup
        {
            public ComponentDataArray<PedestrianData> PedestrianData;
            public ComponentDataArray<Position> Position;
            public ComponentDataArray<Rotation> Rotation;
            public ComponentDataArray<PedestrianState> States;
            public ComponentDataArray<InstanceRendererProperty> StateProperty;
        }
        [Inject] PedestrianGroup _pedestrianGroup;

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
                PedestrianData = _pedestrianGroup.PedestrianData,
                TexelSize = texelSize,
                PatchResolution = patchResolution,
                DeltaTime = Time.deltaTime,
                RdGen = new Random(_randSeed[(Time.frameCount % _capacity)])
            };

            deps = senseJob.Schedule(_capacity, 64, deps);
            
            var stateJob = new PedestrianStateMachineJob()
            {
                PedestrianData = _pedestrianGroup.PedestrianData,
                States = _pedestrianGroup.States,
                StateProperty = _pedestrianGroup.StateProperty,
                RdGen = new Random(_randSeed[(Time.frameCount % _capacity)]),
                DeltaTime = Time.deltaTime,
            };
            deps = stateJob.Schedule(_capacity, 64, deps);

            var moveJob = new PedestrianMoveJob()
            {
                Positions = _pedestrianGroup.Position,
                Rotations = _pedestrianGroup.Rotation,
                PedestrianData = _pedestrianGroup.PedestrianData
            };
            deps = moveJob.Schedule(_capacity, 64, deps);

            return deps;
        }

        [BurstCompile]
        struct PedestrianMoveCheckJob : IJobParallelFor
        {
            public float DeltaTime;

            [ReadOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<ulong> WalkableArea;
            
            public ComponentDataArray<PedestrianData> PedestrianData;

            public float2 TexelSize;
            public int PatchResolution;
            public Random RdGen;

            public void Execute(int index)
            {
                PedestrianData thisPed = PedestrianData[index];
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
                PedestrianData[index] = thisPed;
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
        struct PedestrianStateMachineJob : IJobParallelFor
        {
            public ComponentDataArray<PedestrianData> PedestrianData;
            public ComponentDataArray<PedestrianState> States;
            public ComponentDataArray<InstanceRendererProperty> StateProperty;
            public Random RdGen;
            public float DeltaTime;

            public void Execute(int index)
            {
                int currentState = States[index].State;
                float cd = States[index].CoolDown - DeltaTime;
                if (cd < 0)
                {
                    PedestrianState newstate = States[index];
                    PedestrianData newdata = PedestrianData[index];
                    InstanceRendererProperty newproperty = StateProperty[index];
                    switch (currentState)
                    {
                        case 0://walking
                            ProcessWalkState(newdata, out newstate, out newdata);
                            break;
                        case 1://running
                            ProcessRunState(newdata, out newstate, out newdata);
                            break;
                        case 2://waiting
                            ProcessWaitState(newdata, out newstate, out newdata);
                            break;
                        case 3://standing
                            ProcessStandState(newdata, out newstate, out newdata);
                            break;
                        default:
                            break;
                    }
                    SetAnimProperty(newproperty, newstate, out newproperty);
                    StateProperty[index] = newproperty;
                    PedestrianData[index] = newdata;
                    States[index] = newstate;

                }
                else
                {
                    States[index] = new PedestrianState() {CoolDown = cd, State = currentState};
                }
            }

            private void ProcessWalkState(PedestrianData inputData, out PedestrianState state, out PedestrianData data)
            {
                state = new PedestrianState();
                float randseed = RdGen.NextFloat();
                if (randseed < 0.6f)//to walk again
                {
                    state.State = 0;
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = RdGen.NextFloat(1.0f, 2.0f);
                }
                else if (randseed < 0.8f)//to wait
                {
                    state.State = 2;
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = 0;
                }
                else if (randseed < 0.9f)//to run;
                {
                    state.State = 1;
                    state.CoolDown = RdGen.NextFloat(3.0f, 5.0f);
                    inputData.Speed = RdGen.NextFloat(3.0f, 4.0f);
                }
                else
                {
                    state.State = 3;//to stand
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = 0;
                }

                data = inputData;
            }

            private void ProcessRunState(PedestrianData inputData, out PedestrianState state, out PedestrianData data)
            {
                state = new PedestrianState();
                float randseed = RdGen.NextFloat();
                if (randseed < 0.3f)//to wait
                {
                    state.State = 2;
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = 0;
                }
                else if (randseed < 0.8f)//to walk;
                {
                    state.State = 0;
                    state.CoolDown = RdGen.NextFloat(3.0f, 5.0f);
                    inputData.Speed = RdGen.NextFloat(1.0f, 2.0f);
                }
                else
                {
                    state.State = 3;//to stand
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = 0;
                }

                data = inputData;
            }

            private void ProcessWaitState(PedestrianData inputData, out PedestrianState state, out PedestrianData data)
            {
                state = new PedestrianState();
                float randseed = RdGen.NextFloat();
                if (randseed < 0.8f)//to walk
                {
                    state.State = 0;
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = RdGen.NextFloat(1.0f, 2.0f);
                }
                else if (randseed < 0.9f)//to run;
                {
                    state.State = 1;
                    state.CoolDown = RdGen.NextFloat(3.0f, 5.0f);
                    inputData.Speed = RdGen.NextFloat(3.0f, 4.0f);
                }
                else
                {
                    state.State = 3;//to stand
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = 0;
                }

                data = inputData;
            }

            private void ProcessStandState(PedestrianData inputData, out PedestrianState state, out PedestrianData data)
            {
                state = new PedestrianState();
                float randseed = RdGen.NextFloat();
                if (randseed < 0.8f)//to walk
                {
                    state.State = 0;
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = RdGen.NextFloat(1.0f, 2.0f);
                }
                else if (randseed < 0.9f)//to run;
                {
                    state.State = 1;
                    state.CoolDown = RdGen.NextFloat(3.0f, 5.0f);
                    inputData.Speed = RdGen.NextFloat(3.0f, 4.0f);
                }
                else
                {
                    state.State = 2;//to wait
                    state.CoolDown = RdGen.NextFloat(5.0f, 10.0f);
                    inputData.Speed = 0;
                }

                data = inputData;
            }

            private void SetAnimProperty(InstanceRendererProperty inputproperty, PedestrianState state, out InstanceRendererProperty property)
            {
                switch (state.State)
                {
                    case 0:
                        inputproperty.Value = new float4(0, 25, 0, 0);
                        break;
                    case 1:
                        inputproperty.Value = new float4(26, 57, 0, 0);
                        break;
                    case 2:
                        inputproperty.Value = new float4(58, 330, 0, 0);
                        break;
                    case 3:
                        inputproperty.Value = new float4(331, 511, 0, 0);
                        break;
                    default:
                        inputproperty.Value = new float4(0, 25, 0, 0);
                        break;
                }

                property = inputproperty;
            }
        }

        [BurstCompile]
        struct PedestrianMoveJob : IJobParallelFor
        {
            [WriteOnly]
            public ComponentDataArray<Position> Positions;
            [WriteOnly]
            public ComponentDataArray<Rotation> Rotations;
            [ReadOnly]
            public ComponentDataArray<PedestrianData> PedestrianData;
            
            public void Execute(int index)
            {
                Positions[index] = new Position() {Value = PedestrianData[index].WorldPos};
                Rotations[index] = new Rotation() { Value = quaternion.LookRotation(PedestrianData[index].Forword, new float3(0,1,0))};
            }
        }

        private NativeArray<WalkablePatch> _walkableArea;
        private NativeArray<uint> _randSeed;
    }
}
