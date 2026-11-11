using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.Physics.Authoring
{
    [Serializable]
    public sealed class ColliderGenerationRequest
    {
        public Transform OutputRoot;
        public List<ColliderMeshSource> Sources = new List<ColliderMeshSource>();
        public ColliderGenerationSettings Settings = new ColliderGenerationSettings();
        public int Layer;
        public bool IsTrigger, Enabled = true;
        public PhysicMaterial Material;
        public float ContactOffset = .01f;
        public string MeshAssetDirectory = "Assets/Generated/ColliderAuthoring";
        public bool RejectMirroredOutput;
        public bool RebuildPickingAnalysis;
        public Action<ColliderGenerationResult> AfterApply;
    }

    public sealed class ColliderGenerationResult
    {
        public ColliderGenerationRequest Request { get; internal set; }

        public Collider[] Colliders { get; internal set; }

        public int[] PreviousLayers { get; internal set; }

        public string Summary { get; internal set; }
    }

    public static partial class ColliderGeneration
    {
        public sealed class Preview
        {
            internal PrimitiveColliderFitter.Result Fit;
            internal string GeometryHash;
            internal ColliderGenerationSettings Settings;
            internal Matrix4x4 FitMatrix, OutputMatrix;
            internal string Stamp;
            internal bool ManuallyReused;
            public ColliderGenerationRequest Request { get; internal set; }

            public string Summary => Fit.Note;
            public int Count => Fit.Shapes.Length;
            public int KindCount => Fit.Shapes.Select(s => s.Type).Distinct().Count();
            public int NativeCount => Fit.Shapes.Count(s => s.Type == PrimitiveColliderFitter.Kind.Box ||
                s.Type == PrimitiveColliderFitter.Kind.Sphere || s.Type == PrimitiveColliderFitter.Kind.Capsule);
            public string Composition => PrimitiveColliderFitter.DescribeComposition(Fit.Shapes);
            public string SourceStamp => GeometryHash + ":" + JsonUtility.ToJson(FitMatrix);
            public string ContentHash => HashSnapshot(this);
            public ColliderGeometryGoal Goal => Settings.Goal;
            public bool MatchesSettings(ColliderGenerationSettings settings) =>
                ManuallyReused ? Goal == settings.Goal :
                JsonUtility.ToJson(Settings) == JsonUtility.ToJson(settings) &&
                (settings.Goal != ColliderGeometryGoal.Envelope || NativeCount == Count) &&
                (settings.Goal != ColliderGeometryGoal.Structure || settings.HasCurrentPickingBudget && Count <= settings.TargetCount &&
                    Fit.Shapes.Where(s => s.Type == PrimitiveColliderFitter.Kind.Convex || SixPrimitiveMeshes.Regular(s))
                        .All(s => s.ConvexVertices.Length <= settings.MaxHullVertices));
        }

        public static event Action<ColliderGenerationResult> Applied;
        public static string GetSourceStamp(IReadOnlyList<ColliderMeshSource> sources, Transform output)
        {
            var frame = PhysicalFitMatrix(output);
            return HashGeometry(ColliderGeometry.Read(sources, frame.inverse)) + ":" + JsonUtility.ToJson(frame);
        }
        public static ColliderGenerationRequest CreateRequest(Transform output, ColliderGenerationSettings settings)
        {
            ValidateEditableOutput(output);
            var request = new ColliderGenerationRequest{OutputRoot = output, Settings = settings, Layer = output.gameObject.layer};
            var sample = output.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => !ColliderLegacySources.IsRetained(c));
            if (sample != null)
            {
                request.Material = sample.sharedMaterial;
                request.IsTrigger = sample.isTrigger;
                request.Enabled = sample.enabled;
                request.ContactOffset = sample.contactOffset;
            }

            return request;
        }

        private static string Stamp(Transform output) => string.Join("|", output.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID() + ":" + EditorJsonUtility.ToJson(t) + ":" + t.gameObject.layer + ":" + t.gameObject.activeSelf)) + string.Join("|", output.GetComponentsInChildren<Collider>(true).Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c)));
        public static async Task<Preview> PreviewAsync(ColliderGenerationRequest request, CancellationToken cancellation = default,
            IProgress<ColliderGenerationProgress> progress = null)
        {
            Validate(request);
            var report = new ColliderFitProgress(progress);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            report.Report(0, "读取模型网格");
            var fitMatrix = PhysicalFitMatrix(request.OutputRoot);
            var geometry = ColliderGeometry.Read(request.Sources, fitMatrix.inverse);
            var geometryHash = HashGeometry(geometry);
            report.Report(.03f, "模型网格读取完成");
            var stamp = Stamp(request.OutputRoot);
            var matrix = request.OutputRoot.localToWorldMatrix;
            var settings = request.Settings.Copy();
            bool rebuildPickingAnalysis = request.RebuildPickingAnalysis;
            using var lifetime = new PreviewLifetime(request, cancellation);
            if (settings.Goal == ColliderGeometryGoal.Structure) ValidatePickingBudget(settings);
            string kernel = settings.Goal == ColliderGeometryGoal.Structure ? ReadPickingProxyKernel() : null;
            using var scoring = settings.Goal == ColliderGeometryGoal.Envelope ? ColliderGpuScoring.TryCreate(lifetime.Token) : null;
            EditorApplication.LockReloadAssemblies();
            try
            {
                var fit = await Task.Run(() => settings.Goal == ColliderGeometryGoal.Structure ?
                    FitPickingProxy(geometry, geometryHash, settings, kernel, rebuildPickingAnalysis, report.Slice(.03f, .99f), lifetime.Token) :
                    PrimitiveColliderFitter.Fit(geometry, settings, report.Slice(.03f, .99f), lifetime.Token, scoring));
                lifetime.ThrowIfUnavailable();
                if (settings.Goal == ColliderGeometryGoal.Structure) fit.GenerationSeconds = clock.Elapsed.TotalSeconds;
                report.Report(1, "预览数据已生成");
                return new Preview{Request = request, Fit = fit, GeometryHash = geometryHash, Settings = settings, FitMatrix = fitMatrix, OutputMatrix = matrix, Stamp = stamp};
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        public static async Task<ColliderGenerationResult> GenerateAsync(ColliderGenerationRequest request) => ApplyPreview(await PreviewAsync(request));
        public static ColliderGenerationResult ApplyPreview(Preview preview)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview));
            var request = preview.Request;
            Validate(request);
            if (!preview.MatchesSettings(request.Settings) || !request.OutputRoot.localToWorldMatrix.Equals(preview.OutputMatrix) || Stamp(request.OutputRoot) != preview.Stamp)
                throw new InvalidOperationException("参数、变换或原形状已改变，请重新生成预览。");
            var now = ColliderGeometry.Read(request.Sources, preview.FitMatrix.inverse);
            if (HashGeometry(now) != preview.GeometryHash)
                throw new InvalidOperationException("模型或来源变换已改变，请重新生成。");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("应用通用碰撞体");
            using var proxyAssets = new PickingProxyMeshAssets(request.MeshAssetDirectory);
            try
            {
                var result = new ColliderGenerationResult{Request = request, Colliders = new Collider[preview.Count], Summary = preview.Summary, PreviousLayers = request.OutputRoot.GetComponentsInChildren<Collider>(true).Select(c => c.gameObject.layer).Distinct().ToArray()};
                foreach (var c in request.OutputRoot.GetComponents<Collider>())
                    Undo.DestroyObjectImmediate(c);
                for (int i = request.OutputRoot.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(request.OutputRoot.GetChild(i).gameObject);
                var parent = CreatePhysicalOutputFrame(request.OutputRoot);
                for (int i = 0; i < preview.Count; i++)
                {
                    var s = preview.Fit.Shapes[i];
                    var go = new GameObject($"JulyShape_{s.Type}_{i:D3}");
                    SceneManager.MoveGameObjectToScene(go, request.OutputRoot.gameObject.scene);
                    Undo.RegisterCreatedObjectUndo(go, "创建碰撞体");
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = s.Center;
                    go.transform.localRotation = s.Rotation;
                    go.layer = request.Layer;
                    Collider c;
                    switch (s.Type)
                    {
                        case PrimitiveColliderFitter.Kind.Box:
                            var box = Undo.AddComponent<BoxCollider>(go);
                            box.size = s.Size;
                            c = box;
                            break;
                        case PrimitiveColliderFitter.Kind.Sphere:
                            var sphere = Undo.AddComponent<SphereCollider>(go);
                            sphere.radius = s.Radius;
                            c = sphere;
                            break;
                        case PrimitiveColliderFitter.Kind.Capsule:
                            var cap = Undo.AddComponent<CapsuleCollider>(go);
                            cap.direction = 1;
                            cap.radius = s.Radius;
                            cap.height = s.Height;
                            c = cap;
                            break;
                        case PrimitiveColliderFitter.Kind.Convex:
                            var proxy = Undo.AddComponent<MeshCollider>(go);
                            proxy.convex = true;
                            proxy.sharedMesh = proxyAssets.GetOrCreate(s.ConvexVertices, s.ConvexTriangles);
                            c = proxy;
                            break;
                        case PrimitiveColliderFitter.Kind.Cylinder:
                        case PrimitiveColliderFitter.Kind.Frustum:
                        case PrimitiveColliderFitter.Kind.Prism:
                            var mesh = FineColliderMeshAssets.GetOrCreate(s, request.MeshAssetDirectory, out var scale);
                            go.transform.localScale = scale;
                            var convex = Undo.AddComponent<MeshCollider>(go);
                            convex.convex = true;
                            convex.sharedMesh = mesh;
                            c = convex;
                            break;
                        default:
                            throw new InvalidOperationException("未知形状。");
                    }

                    c.sharedMaterial = request.Material;
                    c.isTrigger = request.IsTrigger;
                    c.enabled = request.Enabled;
                    c.contactOffset = request.ContactOffset;
                    result.Colliders[i] = c;
                }

                request.AfterApply?.Invoke(result);
                Applied?.Invoke(result);
                EditorSceneManager.MarkSceneDirty(request.OutputRoot.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                proxyAssets.Commit();
                return result;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static void Validate(ColliderGenerationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ValidateEditableOutput(request.OutputRoot);
            request.Settings.Validate();
            ColliderAuthoringProfile.ValidateAssetDirectory(request.MeshAssetDirectory);
            if (request.OutputRoot.GetComponentInChildren<Renderer>(true) != null || request.OutputRoot.GetComponentInChildren<Rigidbody>(true) != null)
                throw new ArgumentException("输出分组只能包含形状。");
            if (Mathf.Abs(request.OutputRoot.localToWorldMatrix.determinant) < 1e-10f)
                throw new ArgumentException("输出变换不可逆。");
            if (request.OutputRoot.localToWorldMatrix.determinant < 0 &&
                (request.RejectMirroredOutput || !UniformPhysicalFrame(request.OutputRoot.localToWorldMatrix)))
                throw new ArgumentException("物理输出或需要缩放补偿的输出节点不能使用镜像缩放。");
            if (request.Layer < 0 || request.Layer > 31 || !float.IsFinite(request.ContactOffset) || request.ContactOffset <= 0)
                throw new ArgumentException("图层或接触偏移无效。");
            foreach (var source in request.Sources)
                if (source.Transform.IsChildOf(request.OutputRoot))
                    throw new ArgumentException("来源不能在将被替换的输出分组内。");
        }

        internal static void ValidateEditableOutput(Transform output)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式。");
            if (output == null || EditorUtility.IsPersistent(output))
                throw new ArgumentException("请选择可编辑的输出节点。");
            if (PrefabUtility.IsPartOfPrefabInstance(output) && PrefabStageUtility.GetPrefabStage(output.gameObject) == null)
                throw new InvalidOperationException("请进入Prefab模式。");
        }
    }
}
