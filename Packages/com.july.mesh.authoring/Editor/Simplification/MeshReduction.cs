using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace July.Mesh.Authoring
{
    internal sealed class MeshReduction
    {
        internal int[][] Submeshes;
        internal float Error;
        internal int Triangles => Submeshes.Sum(s => s.Length / 3);

        [DllImport("JulyMeshOptimizer", CallingConvention = CallingConvention.Cdecl)]
        static extern UIntPtr july_mesh_simplify([Out] int[] output, int[] indices, UIntPtr indexCount,
            float[] positions, UIntPtr vertexCount, float[] attributes, float[] weights, UIntPtr attributeCount,
            byte[] locks, UIntPtr targetCount, float error, out float resultError);

        internal static MeshReduction Calculate(MeshGeometry mesh, float ratio, bool automatic, int fidelity, CancellationToken token)
        {
            if (float.IsNaN(ratio) || ratio <= 0 || ratio > 100) throw new ArgumentOutOfRangeException(nameof(ratio));
            if (fidelity < 0 || fidelity > 2) throw new ArgumentOutOfRangeException(nameof(fidelity));
            var result = new MeshReduction { Submeshes = new int[mesh.Submeshes.Length][] };
            if (!automatic && ratio >= 100)
            {
                result.Submeshes = mesh.Submeshes; return result;
            }
            var canonical = mesh.CanonicalVertices();
            var positions = new float[mesh.VertexCount * 3];
            for (int i = 0; i < mesh.VertexCount; i++)
                for (int axis = 0; axis < 3; axis++) positions[i * 3 + axis] = mesh.Positions[i][axis];
            var owners = new Dictionary<Vector3, int>(); var shared = new HashSet<Vector3>();
            for (int sub = 0; sub < mesh.Submeshes.Length; sub++)
                foreach (int index in mesh.Submeshes[sub])
                {
                    Vector3 p = mesh.Positions[index];
                    if (owners.TryGetValue(p, out int owner) && owner != sub) shared.Add(p);
                    else owners[p] = sub;
                }
            var locks = new byte[mesh.VertexCount];
            for (int i = 0; i < locks.Length; i++) if (shared.Contains(mesh.Positions[i])) locks[i] = 1;
            float limit = automatic ? new[] { .001f, .003f, .01f }[fidelity] : float.MaxValue;
            for (int sub = 0; sub < mesh.Submeshes.Length; sub++)
            {
                token.ThrowIfCancellationRequested();
                int[] source = mesh.Submeshes[sub];
                if (source.Length == 0) { result.Submeshes[sub] = Array.Empty<int>(); continue; }
                var indices = source.Select(index => canonical[index]).ToArray();
                var output = new int[indices.Length];
                int target = automatic ? 0 : Math.Max(3, (int)Math.Ceiling(indices.Length / 3.0 * ratio / 100) * 3);
                int count = checked((int)july_mesh_simplify(output, indices, (UIntPtr)(uint)indices.Length, positions,
                    (UIntPtr)(uint)mesh.VertexCount, mesh.Attributes, mesh.Weights, (UIntPtr)(uint)mesh.Weights.Length,
                    locks, (UIntPtr)(uint)target, limit, out float error).ToUInt64());
                if (count < 0 || count > output.Length || count % 3 != 0) throw new InvalidOperationException("meshoptimizer 返回了无效索引数量。");
                Array.Resize(ref output, count); result.Submeshes[sub] = output;
                result.Error = Math.Max(result.Error, error);
            }
            token.ThrowIfCancellationRequested();
            if (result.Triangles == 0) throw new InvalidOperationException("简化结果没有三角面；该目标无法生成可用候选。");
            return result;
        }
    }
}
