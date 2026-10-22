using System;
using System.Collections.Generic;
using UnityEngine;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    // 仅保留旧资源的反序列化结构；新工具不读取、创建或迁移这些记录。
    public sealed class MeshEditRecord : ScriptableObject
    {
        public int Version = 1;
        public UMesh Source;
        public UMesh Original;
        public UMesh Output;
        public string Scope;
        public string ExpectedHash;
        public string SourceHash;
        public string OutputHash;
        public float AppliedRatio = 100;
        public bool Automatic;
        public int Fidelity;
        public string Algorithm = "meshoptimizer 1.0";
        public List<MeshBinding> Bindings = new List<MeshBinding>();
    }

    [Serializable]
    public sealed class MeshBinding
    {
        public string GlobalId;
        public string PrefabGuid;
        public UMesh Original;
        public UMesh Expected;
    }
}
