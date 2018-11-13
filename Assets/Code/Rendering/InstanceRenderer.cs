using System.Collections;
using System.Collections.Generic;
using OSMTrafficSim.BVH;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{
    public class InstanceRenderer
    {
        private Camera ActivaCamera;
        private CommandBuffer cb;
        private Vector4[] param;
        private Matrix4x4[] transforms;
        private NativeArray<float4x4> matrices;
        private NativeArray<float4> propertyParams;
        private MaterialPropertyBlock propertyBlock;

        private int batchCount;
        private int shaderId;
        private InstanceRendererData data;
        public EntityManager manager;

        public void Init(InstanceRendererData renderData, InstanceRendererProperty property)
        {
            data = renderData;
            shaderId = property.ParamId;
            batchCount = 0;
        }

        public void Clean(Camera cam)
        {
            ActivaCamera = cam;
            ActivaCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, cb);
            cb.Clear();
            ActivaCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cb);
        }

        public InstanceRenderer(EntityManager entityManager)
        {
            manager = entityManager;
            param = new Vector4[1023];
            transforms = new Matrix4x4[1023];

            matrices = new NativeArray<float4x4>(1023, Allocator.Temp);
            propertyParams = new NativeArray<float4>(1023, Allocator.Temp);
            propertyBlock = new MaterialPropertyBlock();
            cb = new CommandBuffer();
        }

        private void Submit()
        {
            Utils.CopyToFloat4(propertyParams, param);
            Utils.CopyToFloat4x4(matrices, transforms);
            propertyBlock.SetVectorArray(shaderId, param);
            //cb.DrawMeshInstanced(data.Mesh, data.SubMesh, data.Material, 0, transforms, batchCount, propertyBlock);
            Graphics.DrawMeshInstanced(data.Mesh, data.SubMesh, data.Material, transforms, batchCount, propertyBlock, data.CastShadows, data.ReceiveShadows);
            batchCount = 0;
        }

        public void Batch(Entity ent)
        {
            if (batchCount >= 1023)
            {
                Submit();
            }
            var loc = manager.GetComponentData<LocalToWorld>(ent);
            var prop = manager.GetComponentData<InstanceRendererProperty>(ent);
            matrices[batchCount] = loc.Value;
            propertyParams[batchCount] = prop.Value;
            batchCount++;
        }

        public void Final()
        {
            Submit();
        }

        public void Dispose()
        {
            matrices.Dispose();
            propertyParams.Dispose();
        }
    }
   

}
