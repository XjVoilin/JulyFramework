using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    public sealed class ColliderCurrent
    {
        internal Collider[] Shapes = Array.Empty<Collider>();
        public int Count => Shapes.Length;
        internal int KindCount, NativeCount, MeshCount, DisabledCount;
        internal string Composition = "当前缺失", Details, Stamp, Issue, PreviewNote;
        internal bool ExternalShapes;
        internal ColliderGeometryCounts Geometry;
        public int NativeComponents => NativeCount;
        public int MeshComponents => MeshCount;
        public string Problem => Issue;
        public string Notice { get; internal set; }
        public string Types => Composition;
    }

    // Read-only facts from the authored Prefab, never from preview caches or review choices.
    internal sealed class ColliderInspection
    {
        internal readonly ColliderCurrent[] Current;
        internal readonly string[] SourceStamps;
        internal ColliderAssetLayout Layout;
        internal ColliderInspection(int count) { Current = Enumerable.Range(0, count).Select(_ => new ColliderCurrent()).ToArray(); SourceStamps = new string[count]; }
        internal string ModelStamp, Error;
        internal ColliderGeometryCounts ModelGeometry;
        internal bool Ready => Error == null;

        internal static ColliderInspection Read(string path, IColliderAuthoringAdapter adapter, ColliderAuthoringProfile profile)
        {
            var result = new ColliderInspection(profile.Targets.Length);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var layout = adapter.Inspect(root, profile); result.Layout = layout;
            if (layout.Error != null) { result.Error = layout.Error; return result; }
            result.ModelGeometry = layout.Sources.Aggregate(default(ColliderGeometryCounts),
                (total, source) => total + ColliderGeometryCounts.FromMesh(source.Mesh));
            var item = layout.ItemRoot;
            for (int i = 0; i < profile.Targets.Length; i++)
            {
                var target = layout.Targets[i]; var current = result.Current[i];
                current.Shapes = target.Shapes; current.DisabledCount = target.DisabledCount; current.ExternalShapes = target.ExternalShapes; current.Notice = target.Notice;
                var issues = new List<string>();
                if (target.Issue != null) issues.Add(target.Issue);
                if (current.Shapes.Any(c => !ActiveWithin(c.transform, item))) issues.Add("存在未激活的碰撞体节点");
                var meshShapes = current.Shapes.OfType<MeshCollider>().ToArray();
                current.MeshCount = meshShapes.Length;
                current.Geometry = meshShapes.Aggregate(default(ColliderGeometryCounts),
                    (total, collider) => total + ColliderGeometryCounts.FromMesh(collider.sharedMesh));
                if (meshShapes.Any(c => c.sharedMesh == null)) issues.Add("MeshCollider未绑定Mesh");
                var groups = current.Shapes.GroupBy(c => c.GetType()).ToArray();
                current.KindCount = groups.Length;
                current.NativeCount = current.Shapes.Count(c => c is BoxCollider || c is SphereCollider || c is CapsuleCollider);
                current.Composition = current.Count == 0 ? "当前缺失" : string.Join(" · ", groups.Select(g => TypeName(g.Key) + "×" + g.Count()));
                current.Details = $"启用组件 {current.Count}；禁用备份 {current.DisabledCount}；" +
                    $"Mesh {meshShapes.Length}（Convex {meshShapes.Count(c => c.convex)}）\n" +
                    string.Join("\n", meshShapes.Select(c => c.sharedMesh == null ? "未绑定Mesh" :
                        $"{c.sharedMesh.name}：{c.sharedMesh.vertexCount}顶点，{TriangleCount(c.sharedMesh)}三角形，Convex={c.convex}"));
                if (meshShapes.Any(c => c.convex)) current.PreviewNote = "Convex Mesh显示源网格；实际凸烹制外形尚未核实";
                if (current.Shapes.Any(c => !(c is MeshCollider) && HasShear(c.transform.localToWorldMatrix)))
                    current.PreviewNote = "层级含剪切：原生碰撞体按TRS预览，实际外形未核实" + (current.PreviewNote == null ? "" : "\n" + current.PreviewNote);
                if (current.Shapes.Any(c => !(c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is MeshCollider)))
                    issues.Add("存在预览不支持的Collider类型");
                current.Issue = issues.Count == 0 ? null : string.Join("；", issues);
                try { current.Stamp = Hash(string.Join("|", current.Shapes.Select(c => ShapeStamp(c, item)).OrderBy(s => s, StringComparer.Ordinal))); }
                catch (ArgumentException error) { current.Issue = string.Join("；", issues.Concat(new[] { error.Message })); }
            }
            try
            {
                var sources = layout.Sources;
                var stamps = new Dictionary<Matrix4x4, string>();
                string Source(Transform output)
                {
                    var matrix = output.localToWorldMatrix;
                    if (!stamps.TryGetValue(matrix, out var stamp)) stamps.Add(matrix, stamp = ColliderGeneration.GetSourceStamp(sources, output));
                    return stamp;
                }
                result.ModelStamp = Source(item);
                for (int i = 0; i < profile.Targets.Length; i++) result.SourceStamps[i] = Source(layout.Targets[i].OutputRoot != null ? layout.Targets[i].OutputRoot : layout.Targets[i].MissingFrame);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            { result.Error = error.Message; }
            return result;
        }

        static bool ActiveWithin(Transform node, Transform root)
        {
            while (node != null && node != root) { if (!node.gameObject.activeSelf) return false; node = node.parent; }
            return node == root;
        }

        static bool HasShear(Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > .0001f ||
                Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > .0001f || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > .0001f;
        }

        static ulong TriangleCount(Mesh mesh)
        {
            ulong count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
            return count;
        }

        static string TypeName(Type type) => type == typeof(BoxCollider) ? "盒" : type == typeof(SphereCollider) ? "球" :
            type == typeof(CapsuleCollider) ? "胶囊" : type == typeof(MeshCollider) ? "Mesh" : type.Name;

        [Serializable] sealed class ShapeData
        {
            public string type;
            public Matrix4x4 matrix;
            public Vector3 center, size;
            public float radius, height;
            public int direction;
            public bool convex;
        }

        static string ShapeStamp(Collider collider, Transform item)
        {
            var data = new ShapeData { type = collider.GetType().Name, matrix = item.worldToLocalMatrix * collider.transform.localToWorldMatrix };
            switch (collider)
            {
                case BoxCollider box: data.center = box.center; data.size = box.size; break;
                case SphereCollider sphere: data.center = sphere.center; data.radius = sphere.radius; break;
                case CapsuleCollider capsule: data.center = capsule.center; data.radius = capsule.radius; data.height = capsule.height; data.direction = capsule.direction; break;
                case MeshCollider mesh:
                    data.convex = mesh.convex;
                    return JsonUtility.ToJson(data) + (mesh.sharedMesh == null ? "missing" :
                        ColliderGeneration.GetSourceStamp(new[] { new ColliderMeshSource(mesh.sharedMesh, mesh.transform) }, item));
            }
            return JsonUtility.ToJson(data);
        }

        internal static string Hash(string text)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
    }
}
