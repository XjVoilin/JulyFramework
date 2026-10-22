using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    // Immutable CPU snapshot: Unity reads happen here, the native simplifier only sees arrays.
    internal sealed class MeshGeometry
    {
        internal string Name;
        internal Bounds Bounds;
        internal VertexAttributeDescriptor[] Layout;
        internal byte[][] Streams;
        internal int[] Strides;
        internal Vector3[] Positions;
        internal float[] Attributes;
        internal float[] Weights;
        internal int[][] Submeshes;
        internal int VertexCount => Positions.Length;
        internal int Triangles => Submeshes.Sum(s => s.Length / 3);

        internal static MeshGeometry Read(UMesh mesh)
        {
            if (!mesh || mesh.vertexCount == 0) throw new ArgumentException("Mesh 没有顶点。");
            if (mesh.blendShapeCount != 0 || mesh.HasVertexAttribute(VertexAttribute.BlendIndices) || mesh.HasVertexAttribute(VertexAttribute.BlendWeight))
                throw new NotSupportedException("首版不处理骨骼权重或 BlendShape Mesh。");
            var result = new MeshGeometry { Name = mesh.name, Bounds = mesh.bounds, Layout = mesh.GetVertexAttributes(), Submeshes = new int[mesh.subMeshCount][] };
            using (var snapshots = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data = snapshots[0];
                result.Streams = new byte[data.vertexBufferCount][];
                result.Strides = new int[data.vertexBufferCount];
                for (int stream = 0; stream < result.Streams.Length; stream++)
                {
                    result.Streams[stream] = data.GetVertexData<byte>(stream).ToArray();
                    result.Strides[stream] = data.GetVertexBufferStride(stream);
                }
                using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(positions); result.Positions = positions.ToArray();
                }
                for (int sub = 0; sub < data.subMeshCount; sub++)
                {
                    var descriptor = data.GetSubMesh(sub);
                    if (descriptor.topology != MeshTopology.Triangles || descriptor.indexCount % 3 != 0)
                        throw new NotSupportedException("仅支持三角形子网格。");
                    using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, sub, true); result.Submeshes[sub] = indices.ToArray();
                    }
                }
                var channels = new List<float[]>();
                var weights = new List<float>();
                if (data.HasVertexAttribute(VertexAttribute.Normal))
                {
                    using (var values = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetNormals(values);
                        for (int axis = 0; axis < 3; axis++)
                        {
                            var channel = new float[data.vertexCount];
                            for (int i = 0; i < channel.Length; i++) channel[i] = values[i][axis];
                            channels.Add(channel); weights.Add(.5f);
                        }
                    }
                }
                for (int uv = 0; uv < 8; uv++)
                {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + uv);
                    if (!data.HasVertexAttribute(attribute)) continue;
                    using (var values = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetUVs(uv, values);
                        for (int axis = 0; axis < data.GetVertexAttributeDimension(attribute); axis++)
                        {
                            var channel = new float[data.vertexCount];
                            for (int i = 0; i < channel.Length; i++) channel[i] = values[i][axis];
                            channels.Add(channel); weights.Add(10f);
                        }
                    }
                }
                if (data.HasVertexAttribute(VertexAttribute.Color))
                {
                    using (var values = new NativeArray<Color>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetColors(values);
                        for (int axis = 0; axis < 4; axis++)
                        {
                            var channel = new float[data.vertexCount];
                            for (int i = 0; i < channel.Length; i++) channel[i] = values[i][axis];
                            channels.Add(channel); weights.Add(1f);
                        }
                    }
                }
                if (channels.Count > 32) throw new NotSupportedException("此 Mesh 的法线、UV 与颜色超过 meshoptimizer 的 32 个属性分量上限。");
                result.Weights = weights.ToArray();
                result.Attributes = new float[data.vertexCount * channels.Count];
                for (int vertex = 0; vertex < data.vertexCount; vertex++)
                    for (int channel = 0; channel < channels.Count; channel++)
                        result.Attributes[vertex * channels.Count + channel] = channels[channel][vertex];
            }
            if (result.Triangles == 0) throw new ArgumentException("Mesh 没有三角面。");
            foreach (var p in result.Positions)
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new ArgumentException("Mesh 顶点包含非有限数值。");
            foreach (float value in result.Attributes)
                if (!Finite(value)) throw new ArgumentException("Mesh 属性包含非有限数值。");
            foreach (var sub in result.Submeshes)
                foreach (int index in sub)
                    if (index < 0 || index >= result.VertexCount) throw new ArgumentException("Mesh 三角面索引越界。");
            return result;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal string Fingerprint()
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            using (var hash = SHA256.Create())
            {
                writer.Write(VertexCount); writer.Write(Layout.Length);
                foreach (var a in Layout) { writer.Write((int)a.attribute); writer.Write((int)a.format); writer.Write(a.dimension); writer.Write(a.stream); }
                foreach (var stream in Streams) { writer.Write(stream.Length); writer.Write(stream); }
                writer.Write(Submeshes.Length);
                foreach (var sub in Submeshes) { writer.Write(sub.Length); foreach (int index in sub) writer.Write(index); }
                for (int i = 0; i < 3; i++) { writer.Write(Bounds.center[i]); writer.Write(Bounds.size[i]); }
                writer.Flush(); return BitConverter.ToString(hash.ComputeHash(memory.ToArray())).Replace("-", "");
            }
        }

        internal UMesh Build(int[][] submeshes)
        {
            var map = new int[VertexCount]; Array.Fill(map, -1);
            var used = new List<int>();
            var indices = new List<int>(); var ranges = new List<SubMeshDescriptor>();
            foreach (var sub in submeshes)
            {
                int start = indices.Count;
                foreach (int old in sub)
                {
                    if (map[old] < 0) { map[old] = used.Count; used.Add(old); }
                    indices.Add(map[old]);
                }
                ranges.Add(new SubMeshDescriptor(start, sub.Length, MeshTopology.Triangles));
            }
            var mesh = new UMesh { name = Name + "_Simplified" };
            mesh.SetVertexBufferParams(used.Count, Layout);
            for (int stream = 0; stream < Streams.Length; stream++)
            {
                int stride = Strides[stream]; var bytes = new byte[used.Count * stride];
                for (int i = 0; i < used.Count; i++) Buffer.BlockCopy(Streams[stream], used[i] * stride, bytes, i * stride, stride);
                mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream, MeshUpdateFlags.DontRecalculateBounds);
            }
            mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices.ToArray(), 0, 0, indices.Count, MeshUpdateFlags.DontRecalculateBounds);
            mesh.subMeshCount = ranges.Count;
            for (int i = 0; i < ranges.Count; i++) mesh.SetSubMesh(i, ranges[i]);
            mesh.bounds = Bounds;
            return mesh;
        }

        // Welding is allowed only when every original vertex byte matches: UV/normal seams survive.
        internal int[] CanonicalVertices()
        {
            var result = new int[VertexCount];
            var lookup = new Dictionary<int, int>(new VertexComparer(this));
            for (int i = 0; i < VertexCount; i++)
            {
                if (!lookup.TryGetValue(i, out int first)) { first = i; lookup.Add(i, i); }
                result[i] = first;
            }
            return result;
        }

        sealed class VertexComparer : IEqualityComparer<int>
        {
            readonly MeshGeometry mesh;
            internal VertexComparer(MeshGeometry mesh) { this.mesh = mesh; }
            public bool Equals(int a, int b)
            {
                for (int s = 0; s < mesh.Streams.Length; s++)
                    for (int i = 0; i < mesh.Strides[s]; i++)
                        if (mesh.Streams[s][a * mesh.Strides[s] + i] != mesh.Streams[s][b * mesh.Strides[s] + i]) return false;
                return true;
            }
            public int GetHashCode(int vertex)
            {
                unchecked
                {
                    int hash = 17;
                    for (int s = 0; s < mesh.Streams.Length; s++)
                        for (int i = 0; i < mesh.Strides[s]; i++) hash = hash * 31 + mesh.Streams[s][vertex * mesh.Strides[s] + i];
                    return hash;
                }
            }
        }
    }
}
