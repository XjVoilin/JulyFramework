using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    // Shared, immutable, content-addressed generated meshes. Successful Undo
    // leaves them available for Redo and other prefabs. Failed Apply removes
    // only assets created by that Apply, after the old group is restored.
    internal sealed class PickingProxyMeshAssets : IDisposable
    {
        private readonly string _folder;
        internal PickingProxyMeshAssets(string folder){_folder=folder;}
        private readonly List<string> _created=new List<string>();
        private bool _committed;
        internal Mesh GetOrCreate(Vector3[] vertices,int[] triangles)
        {
            byte[] bytes;
            using(var memory=new MemoryStream())
            {
                using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))
                {writer.Write(vertices.Length);foreach(var p in vertices){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}writer.Write(triangles.Length);foreach(int i in triangles)writer.Write(i);}
                bytes=memory.ToArray();
            }
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            string path=_folder+"/PickingProxy_v1_"+hash+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing!=null)
            {
                if(!existing.isReadable||!existing.vertices.SequenceEqual(vertices)||!existing.triangles.SequenceEqual(triangles))
                    throw new InvalidOperationException("已生成的共享凸网格被修改，请检查资源："+path);
                return existing;
            }
            if(File.Exists(path))throw new InvalidOperationException("凸网格路径已有其他资源，拒绝覆盖："+path);
            FineColliderMeshAssets.EnsureFolder(_folder);
            var mesh=new Mesh{name="PickingConvex_"+hash,vertices=vertices,triangles=triangles};mesh.RecalculateBounds();mesh.RecalculateNormals();
            try{AssetDatabase.CreateAsset(mesh,path);if(!EditorUtility.IsPersistent(mesh))throw new IOException("无法保存生成的凸网格："+path);_created.Add(path);UnityEngine.Physics.BakeMesh(mesh.GetInstanceID(),true);return mesh;}
            catch{if(!EditorUtility.IsPersistent(mesh))UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }
        internal void Commit(){_committed=true;}
        public void Dispose()
        {
            if(_committed)return;
            foreach(string path in _created)if(!AssetDatabase.DeleteAsset(path))
                Debug.LogError("应用失败后未能清理本次新建的凸网格："+path);
        }
    }
}
