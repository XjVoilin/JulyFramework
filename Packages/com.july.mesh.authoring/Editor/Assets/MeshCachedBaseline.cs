using System;
using UnityEngine;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    internal sealed class MeshCachedBaseline : ScriptableObject, IDisposable
    {
        public int Version = 1;
        public UMesh Original;
        public string ExpectedHash;
        public bool Applied;
        public float Ratio = 100;
        public bool Automatic;
        public int Fidelity;
        public void Dispose()
        {
            if (Original) DestroyImmediate(Original);
            DestroyImmediate(this);
        }
    }

}
