using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    internal sealed class MeshRow : IDisposable
    {
        internal UMesh Source;
        internal string ScopeLabel;
        internal MeshCachedBaseline Cache;
        internal MeshGeometry Geometry;
        internal string ObservedHash, CandidateHash, BaselineHash;
        internal UMesh CurrentPreview, Candidate;
        internal MeshReduction Recommendation;
        internal int RecommendedFidelity = -1;
        internal float Ratio = 100, AppliedRatio = 100;
        internal int AppliedFidelity, CandidateFidelity;
        internal bool Automatic = true, AppliedAutomatic, HasApplied;
        internal bool Checked = true, Busy, NeedsSourceCheck;
        internal string Error, ApplyError;
        internal int Revision, VisualVersion;
        internal Request Pending;
        internal int OriginalTriangles => Geometry == null ? 0 : Geometry.Triangles;
        internal int CurrentTriangles, CandidateTriangles;
        internal float ActualRatio => OriginalTriangles == 0 ? 100 : CandidateTriangles * 100f / OriginalTriangles;
        internal float RecommendedRatio => Recommendation == null ? 100 : Recommendation.Triangles * 100f / OriginalTriangles;
        internal bool HasChanges => Candidate && (CandidateHash != ObservedHash || HasApplied
            && (Mathf.Abs(Ratio - AppliedRatio) > .001f || Automatic != AppliedAutomatic || Automatic && CandidateFidelity != AppliedFidelity));
        internal bool CanRestore => HasApplied;

        internal void Load(bool force = false)
        {
            string observed = MeshGeometry.Read(Source).Fingerprint();
            if (!force && Cache && !NeedsSourceCheck && observed == ObservedHash) return;
            var next = MeshAssets.Baseline(Source);
            if (Cache) Cache.Dispose(); Cache = next;
            var geometry = MeshGeometry.Read(Cache.Original);
            string baselineHash = geometry.Fingerprint();
            if (BaselineHash != baselineHash) { Recommendation = null; RecommendedFidelity = -1; }
            Geometry = geometry; BaselineHash = baselineHash;
            var current = MeshGeometry.Read(Source); ObservedHash = current.Fingerprint(); CurrentTriangles = current.Triangles;
            HasApplied = Cache.Applied; AppliedRatio = Cache.Automatic && HasApplied ? CurrentTriangles * 100f / OriginalTriangles : Cache.Ratio;
            AppliedAutomatic = Cache.Automatic; AppliedFidelity = Cache.Fidelity; CandidateFidelity = AppliedFidelity;
            Ratio = AppliedRatio; Automatic = HasApplied ? AppliedAutomatic : true;
            if (CurrentPreview) UObject.DestroyImmediate(CurrentPreview);
            if (Candidate) UObject.DestroyImmediate(Candidate);
            CurrentPreview = UObject.Instantiate(Source); Candidate = UObject.Instantiate(Source);
            CandidateTriangles = CurrentTriangles; CandidateHash = ObservedHash;
            NeedsSourceCheck = false; VisualVersion++;
        }

        internal void SetCandidate(MeshReduction reduction, bool restore = false)
        {
            var mesh = restore ? UObject.Instantiate(Cache.Original) : Geometry.Build(reduction.Submeshes);
            if (Candidate) UObject.DestroyImmediate(Candidate);
            Candidate = mesh; CandidateTriangles = MeshGeometry.Read(mesh).Triangles;
            CandidateHash = MeshGeometry.Read(mesh).Fingerprint(); VisualVersion++; Error = null;
        }

        public void Dispose()
        {
            Revision++; Pending = null;
            if (CurrentPreview) UObject.DestroyImmediate(CurrentPreview);
            if (Candidate) UObject.DestroyImmediate(Candidate);
            if (Cache) Cache.Dispose(); Cache = null;
        }
        internal sealed class Request
        {
            internal MeshRow Row; internal int Revision; internal float Ratio;
            internal bool Automatic, Show; internal int Fidelity;
        }
    }

    internal sealed class MeshWorkspace : IDisposable
    {
        internal readonly List<MeshRow> Rows = new List<MeshRow>();
        internal event Action Changed;
        internal int Fidelity;
        internal bool Working => running != null || Rows.Any(row => row.Pending != null);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        Task<MeshReduction> running;
        MeshRow.Request active;
        bool disposed;

        internal void Initialize(MeshRow row)
        {
            try { row.Load(); Request(row, 100, true, !row.HasApplied); }
            catch (Exception error) { row.Error = error.Message; Changed?.Invoke(); }
        }
        internal void Request(MeshRow row, float ratio, bool automatic, bool show = true)
        {
            row.Revision++; row.Error = null;
            row.Pending = new MeshRow.Request { Row = row, Revision = row.Revision, Ratio = ratio, Automatic = automatic, Show = show, Fidelity = Fidelity };
            row.Busy = true;
            if (show) { row.Ratio = ratio; row.Automatic = automatic; }
            Changed?.Invoke();
        }
        internal void RestoreRecommendation(MeshRow row)
        {
            if (row.Geometry == null || row.NeedsSourceCheck) row.Load();
            row.Revision++; row.Pending = null;
            if (row.Recommendation != null && row.RecommendedFidelity == Fidelity)
            {
                row.Automatic = true; row.Ratio = row.RecommendedRatio;
                row.SetCandidate(row.Recommendation); row.CandidateFidelity = Fidelity; row.Busy = false; Changed?.Invoke();
            }
            else Request(row, 100, true);
        }
        internal void Tick()
        {
            if (disposed) return;
            if (running != null && running.IsCompleted)
            {
                var request = active; Complete(running, request); running = null; active = null; Changed?.Invoke();
            }
            if (running != null) return;
            var next = Rows.FirstOrDefault(row => row.Pending != null);
            if (next == null) return;
            active = next.Pending; next.Pending = null;
            try
            {
                if (next.Geometry == null || next.NeedsSourceCheck) next.Load();
                var request = active; var geometry = next.Geometry; var token = lifetime.Token;
                if (request.Show) { next.Ratio = request.Ratio; next.Automatic = request.Automatic; }
                running = Task.Run(() => MeshReduction.Calculate(geometry, request.Ratio, request.Automatic, request.Fidelity, token), token);
            }
            catch (Exception error) { next.Busy = false; next.Error = error.Message; active = null; Changed?.Invoke(); }
        }
        void Complete(Task<MeshReduction> task, MeshRow.Request request)
        {
            var row = request.Row;
            try
            {
                var reduction = task.GetAwaiter().GetResult();
                if (row.Revision != request.Revision || !Rows.Contains(row)) return;
                if (request.Automatic)
                {
                    row.Recommendation = reduction; row.RecommendedFidelity = request.Fidelity;
                    if (request.Show) row.Ratio = row.RecommendedRatio;
                }
                if (request.Show) { row.SetCandidate(reduction, !request.Automatic && request.Ratio >= 100); row.CandidateFidelity = request.Fidelity; }
                row.Busy = false;
            }
            catch (OperationCanceledException) { if (row.Revision == request.Revision) row.Busy = false; }
            catch (Exception error) { if (row.Revision == request.Revision) { row.Busy = false; row.Error = error.Message; } }
        }
        internal void Clear() { foreach (var row in Rows) row.Dispose(); Rows.Clear(); Changed?.Invoke(); }
        public void Dispose()
        {
            disposed = true; lifetime.Cancel();
            if (running != null) running.ContinueWith(task => { var observed = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            Clear(); lifetime.Dispose();
        }
    }

    internal static class MeshSelection
    {
        internal static List<MeshRow> Collect(UObject[] selection, out int unsupportedCount)
        {
            var unsupported = new HashSet<UMesh>();
            var rows = new Dictionary<string, MeshRow>();
            void Add(UMesh mesh)
            {
                if (!mesh) return;
                if (!MeshAssetEdits.Standalone(mesh)) { unsupported.Add(mesh); return; }
                string path = AssetDatabase.GetAssetPath(mesh);
                string identity = MeshAssetEdits.Identity(mesh);
                if (rows.ContainsKey(identity)) return;
                var row = new MeshRow { Source = mesh, ScopeLabel = "资源级修改，影响全部引用" };
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) { row.ApplyError = "只允许处理 Assets 内可写来源：" + path; row.Checked = false; }
                rows.Add(identity, row);
            }
            void Asset(string path)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && root)
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) Add(filter.sharedMesh);
                else foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<UMesh>()) Add(mesh);
            }
            foreach (var value in selection)
            {
                string path = AssetDatabase.GetAssetPath(value);
                if (AssetDatabase.IsValidFolder(path))
                    foreach (string guid in AssetDatabase.FindAssets("t:Mesh t:Model t:Prefab", new[] { path })) Asset(AssetDatabase.GUIDToAssetPath(guid));
                else if (value is GameObject root && !EditorUtility.IsPersistent(root))
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) Add(filter.sharedMesh);
                else if (value is UMesh mesh) Add(mesh);
                else if (value is GameObject || !string.IsNullOrEmpty(path)) Asset(path);
            }
            unsupportedCount = unsupported.Count;
            return rows.Values.OrderBy(row => AssetDatabase.GetAssetPath(row.Source)).ToList();
        }
    }
}
