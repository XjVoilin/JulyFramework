using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    public enum ColliderProgress { None, Queued, Running, Complete, Cancelled, Failed }

    public sealed class ColliderCandidate
    {
        public ColliderGeneration.Preview Preview { get; internal set; }
        public string Error { get; internal set; }
        public bool Applied { get; internal set; }
        internal string TargetId;
        internal ColliderGeometryGoal Goal;
        internal string Revision, SavedAt;
        internal Matrix4x4 WorldToItem;
        public ColliderProgress Progress { get; internal set; }
        public string Stage { get; internal set; }
        public float Fraction { get; internal set; }
        internal readonly System.Diagnostics.Stopwatch Timer = new System.Diagnostics.Stopwatch();
    }

    public sealed class ColliderRow
    {
        internal ColliderWorkspace Owner;
        internal ColliderAuthoringProfile Profile => Owner.Profile;
        internal string Path, Error, CacheError, ObservedRevision;
        internal bool Selected;
        internal ColliderTargetSelection GenerateScope, ApplyScope;
        internal string Guid;
        internal ColliderInspection Inspection;
        internal ColliderReviewRecord Review;
        internal bool HasCacheHistory;
        internal readonly Dictionary<string, ColliderCandidate> CandidateStore = new Dictionary<string, ColliderCandidate>();
        ColliderCandidate[] activeCandidates;
        internal ColliderCandidate[] Candidates => activeCandidates ??= Profile.Targets.Select(t => GetCandidate(t.Id, t.Settings.Goal)).ToArray();
        internal ColliderCandidate GetCandidate(string id, ColliderGeometryGoal goal)
        {
            string key = id + ":" + (int)goal;
            if (!CandidateStore.TryGetValue(key, out var value)) CandidateStore.Add(key, value = new ColliderCandidate { TargetId = id, Goal = goal });
            return value;
        }
        public string AssetPath => Path;
        public string AssetGuid => Guid;
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        public bool Checked { get => Selected; set => Selected = value; }
        public IReadOnlyList<ColliderCurrent> Current => Inspection.Current;
        public IReadOnlyList<ColliderCandidate> ActiveCandidates => Candidates;
        public bool IsNew => Review != null && Review.isNew;
        public bool IsConfirmed => Confirmed;
        public bool RequiresReview(string targetId) => NeedsReview(Array.FindIndex(Profile.Targets, t => t.Id == targetId));
        public bool HasCurrentCandidate(string targetId) => IsCurrent(Array.FindIndex(Profile.Targets, t => t.Id == targetId));
        public ColliderGenerationSettings GenerationSettings(string targetId) => EffectiveSettings(Owner.Indices(ColliderTargetSelection.Only(targetId)).Single());
        internal ColliderItemRecipe Recipe(int index) => Review?.recipes.SingleOrDefault(r => r.targetId == Profile.Targets[index].Id && r.goal == Profile.Targets[index].Settings.Goal);
        internal ColliderGenerationSettings EffectiveSettings(int index)
        {
            var settings = Profile.Targets[index].Settings.Copy();
            var recipe = Recipe(index);
            if (recipe != null)
            {
                settings.TargetCount = recipe.targetCount; settings.SilhouetteErrorPercent = recipe.silhouetteErrorPercent;
                if (recipe.overrideMeshBudget) { settings.MaxHullVertices = recipe.maxHullVertices; settings.PickingInwardErrorPercent = recipe.pickingInwardErrorPercent; }
                if (recipe.overridePickingShapes) settings.PickingShapes = recipe.pickingShapes;
                if (recipe.overrideNativeTolerance) { settings.NativeShapeErrorPercent = recipe.nativeShapeErrorPercent; settings.NativeClickChangePercent = recipe.nativeClickChangePercent; }
            }
            if (settings.Goal == ColliderGeometryGoal.Structure)
                settings.TargetCount = Math.Min(settings.TargetCount, PickingProxy.GenerationSettings.ConvexCountLimit);
            return settings;
        }
        internal Vector2 Rotation = new Vector2(-15, 145);
        internal bool IsCurrent(int index) => Candidates[index].Preview != null && Inspection.Ready &&
            Candidates[index].Preview.SourceStamp == Inspection.SourceStamps[index] && Candidates[index].Preview.MatchesSettings(EffectiveSettings(index));
        internal ColliderReviewGoal ReviewGoal(int index) => Review.Goal(Profile.Targets[index].Id);
        internal ColliderDecision Decision(int index) => Review == null ? ColliderDecision.Pending : ReviewGoal(index).decision;
        internal bool NeedsReview(int index) => Review != null && Decision(index) == ColliderDecision.Keep &&
            (ReviewGoal(index).model != Inspection.ModelStamp || ReviewGoal(index).current != Inspection.Current[index].Stamp);
        internal bool GroupConfirmed(int index) => Decision(index) == ColliderDecision.Keep && !NeedsReview(index) &&
            Inspection.Ready && Inspection.Current[index].Count > 0 && Inspection.Current[index].Issue == null;
        internal bool Confirmed => Enumerable.Range(0, Profile.Targets.Length).All(GroupConfirmed);
        internal bool CanApply(int index) => Review != null && IsCurrent(index) && !Inspection.Current[index].ExternalShapes;
        internal int[] CandidateIndices(ColliderTargetSelection scope) => Owner.Indices(scope).Where(i => Candidates[i].Preview != null).ToArray();
        public IEnumerable<string> Errors
        {
            get
            {
                if (!string.IsNullOrEmpty(Error)) yield return Error;
                if (!string.IsNullOrEmpty(CacheError)) yield return CacheError;
                if (!string.IsNullOrEmpty(Inspection.Error)) yield return Inspection.Error;
                foreach (var candidate in Candidates) if (!string.IsNullOrEmpty(candidate.Error)) yield return candidate.Error;
                foreach (var current in Inspection.Current) if (!string.IsNullOrEmpty(current.Issue)) yield return current.Issue;
            }
        }
        internal bool InQueue(ColliderQueue queue) => queue switch
        {
            ColliderQueue.All => true,
            ColliderQueue.Pending => !Confirmed,
            ColliderQueue.Done => Confirmed,
            ColliderQueue.New => Review != null && Review.isNew && !Confirmed,
            ColliderQueue.Recheck => Enumerable.Range(0, Profile.Targets.Length).Any(NeedsReview),
            ColliderQueue.Issues => Errors.Any() || !Inspection.Ready || Inspection.Current.Any(c => c.Count == 0),
            _ => throw new ArgumentOutOfRangeException(nameof(queue))
        };
    }

    // Owns data and one serial generation queue, never a live scene per list entry.
    public sealed class ColliderWorkspace : IDisposable
    {
        internal readonly List<ColliderRow> Rows = new List<ColliderRow>();
        // Result/queue changes invalidate statistics; work-bar ticks are read from ActiveCandidates.
        public event Action Changed;
        public bool Busy { get; private set; }
        public string Message { get; private set; } = "在 Project 选择 Prefab 或文件夹，再读取所选。";
        CancellationTokenSource cancellation;
        bool disposed;
        internal bool IsDisposed => disposed;
        ColliderReviews reviews;
        readonly string reviewPath;
        internal readonly IColliderAuthoringAdapter Adapter;
        internal ColliderAuthoringProfile Profile { get; private set; }
        public ColliderAuthoringProfile Configuration => Profile.Copy();
        public IReadOnlyList<ColliderRow> Items => Rows;
        string[] selectionPaths = Array.Empty<string>();
        internal string[] SkippedReasons { get; private set; } = Array.Empty<string>();
        public ColliderWorkspace(IColliderAuthoringAdapter adapter = null, string reviewPath = null)
        {
            Adapter = adapter ?? new DefaultColliderAuthoringAdapter(); Profile = Adapter.CreateProfile(); ColliderAuthoringSettings.Load(Profile);
            this.reviewPath = reviewPath; reviews = new ColliderReviews(Profile, reviewPath);
        }
        internal IEnumerable<int> Indices(ColliderTargetSelection scope)
        {
            if (scope.TargetId != null && !Profile.Targets.Any(t => t.Id == scope.TargetId)) throw new ArgumentException("输出目标不存在。");
            return Enumerable.Range(0, Profile.Targets.Length).Where(i => scope.TargetId == null || Profile.Targets[i].Id == scope.TargetId);
        }
        internal ColliderTargetSelection ValidScope(ColliderTargetSelection scope) => scope.TargetId == null || Profile.Targets.Any(t => t.Id == scope.TargetId) ? scope : ColliderTargetSelection.All;
        internal string ScopeName(ColliderTargetSelection scope) => scope.TargetId == null ? string.Join("＋", Profile.Targets.Select(t => t.Name)) :
            "仅" + Profile.Targets.Single(t => t.Id == scope.TargetId).Name;
        public void ChangeGoal(string targetId, ColliderGeometryGoal goal)
        {
            if (Busy) throw new InvalidOperationException("生成期间不能修改生成方式。");
            Profile.Targets.Single(t => t.Id == targetId).ChangeGoal(goal); ColliderAuthoringSettings.SaveView(Profile); RefreshSelection();
        }
        public void ReloadConfiguration()
        {
            if (Busy) throw new InvalidOperationException("生成期间不能修改制作配置。");
            Profile = Adapter.CreateProfile(); ColliderAuthoringSettings.Load(Profile); reviews = new ColliderReviews(Profile, reviewPath); RefreshSelection();
        }
        public void Configure(ColliderAuthoringProfile edited)
        {
            if (Busy) throw new InvalidOperationException("生成期间不能修改制作配置。");
            if (edited.Id != Profile.Id) throw new ArgumentException("配置不属于当前适配。");
            var next = Adapter.CreateProfile();
            next.MeshDirectory = edited.MeshDirectory;
            next.Performance = edited.Performance.Copy();
            if (next.EditableTargets) { next.SourcePath = edited.SourcePath; next.Targets = edited.Targets; }
            ColliderAuthoringSettings.Save(next); ColliderAuthoringSettings.SaveView(next); ReloadConfiguration();
        }

        List<ColliderRow> Collect(Object[] selection, out int skipped, out string[] skippedReasons)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var reasons = new List<string>();
            skipped = 0;
            foreach (var asset in selection)
            {
                var path = AssetDatabase.GetAssetPath(asset);
                if (AssetDatabase.IsValidFolder(path))
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { path }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                else if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
                else { skipped++; reasons.Add(path + "：不是Prefab或目录"); }
            }
            var rows = new List<ColliderRow>();
            foreach (var path in paths.OrderBy(p => p, Comparer<string>.Create(EditorUtility.NaturalCompare)))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !Adapter.CanHandle(prefab))
                { skipped++; reasons.Add(path + "：不符合当前制作适配"); continue; }
                rows.Add(new ColliderRow { Owner = this, Path = path, Guid = AssetDatabase.AssetPathToGUID(path) });
            }
            skippedReasons = reasons.ToArray();
            return rows;
        }

        public void Read(Object[] selection)
        {
            if (Busy) throw new InvalidOperationException("生成期间不能重新读取资源。");
            // Observe external author-data repairs without discarding a failed save awaiting retry.
            if (!reviews.Dirty) reviews = new ColliderReviews(Profile, reviewPath);
            var previous = Rows.ToDictionary(r => r.Guid);
            selectionPaths = selection.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            var rows = Collect(selection, out var skipped, out var skippedReasons);
            Rows.Clear();
            Rows.AddRange(rows);
            foreach (var row in Rows)
            {
                row.ObservedRevision = Revision(row.Path);
                row.Inspection = ColliderInspection.Read(row.Path, row.Owner.Adapter, row.Profile);
                if (previous.TryGetValue(row.Guid, out var old))
                {
                    row.Selected = old.Selected; row.Rotation = old.Rotation;
                    row.GenerateScope = ValidScope(old.GenerateScope); row.ApplyScope = ValidScope(old.ApplyScope);
                }
                ColliderCache.Load(row);
            }
            reviews.Register(Rows);
            if (reviews.Dirty) reviews.Save();
            SkippedReasons = skippedReasons;
            Message = $"已读取 {Rows.Count} 个物品，跳过 {skipped} 项。生成预览不会修改 Prefab。";
            Notify();
        }

        public void RefreshSelection()
        {
            if (selectionPaths.Length == 0) return;
            Read(selectionPaths.Select(AssetDatabase.LoadAssetAtPath<Object>).Where(a => a != null).ToArray());
        }

        // Saving generated mesh assets also raises projectChanged. Keep the live
        // rows and their selections, candidates and feedback when the item list
        // is unchanged; only a changed catalog requires rebuilding that list.
        internal void RefreshProjectChanges()
        {
            if (selectionPaths.Length == 0) return;
            var selection = selectionPaths.Select(AssetDatabase.LoadAssetAtPath<Object>).Where(a => a != null).ToArray();
            var items = Collect(selection, out _, out _);
            if (!new HashSet<(string, string)>(items.Select(r => (r.Guid, r.Path))).SetEquals(Rows.Select(r => (r.Guid, r.Path)))) Read(selection);
            else RefreshCurrent();
        }

        public void RefreshCurrent()
        {
            foreach (var row in Rows) RefreshCurrent(row);
            Notify();
        }

        internal static void RefreshCurrent(ColliderRow row)
        {
            row.ObservedRevision = Revision(row.Path);
            row.Inspection = ColliderInspection.Read(row.Path, row.Owner.Adapter, row.Profile);
        }

        public void Decide(ColliderRow row, int index, ColliderDecision decision)
        {
            if (Busy) throw new InvalidOperationException("生成期间不能修改审阅选择。");
            if (row.Review == null) throw new InvalidOperationException("资源尚未完成登记，请重新读取。");
            RefreshCurrent(row);
            if (decision == ColliderDecision.Keep && row.Inspection.Current[index].Count == 0)
                throw new InvalidOperationException("当前组缺失，不能确认保留。");
            var previous = row.ReviewGoal(index);
            bool wasNew = row.Review.isNew;
            SetDecision(row, index, decision);
            ColliderReviews.CompleteRegistration(row);
            try { reviews.Save(); }
            catch { row.Review.Set(previous); row.Review.isNew = wasNew; throw; }
            Message = "审阅选择已保存，尚未写回Prefab。";
            Notify();
        }

        public void MarkHandled(ColliderRow row)
        {
            ValidateAuthorEdit(row);
            var displayed = row.Inspection;
            RefreshCurrent(row);
            if (displayed.ModelStamp != row.Inspection.ModelStamp || !displayed.Current.Select(c => c.Stamp).SequenceEqual(row.Inspection.Current.Select(c => c.Stamp)))
                throw new InvalidOperationException("当前结果已变化，请查看刷新后的结果，再标记已处理。");
            if (!row.Inspection.Ready || row.Inspection.Current.Any(c => c.Count == 0 || c.Issue != null))
                throw new InvalidOperationException("当前所有输出组均须完整且配置正常，才能标记已处理。");
            var previous = row.Review.goals.ToArray();
            bool wasNew = row.Review.isNew;
            for (int i = 0; i < Profile.Targets.Length; i++) SetDecision(row, i, ColliderDecision.Keep);
            ColliderReviews.CompleteRegistration(row);
            try { reviews.Save(); }
            catch { row.Review.goals = previous; row.Review.isNew = wasNew; throw; }
            Message = "已标记处理完成，保留当前碰撞体；没有修改Prefab或候选缓存。";
            Notify();
        }

        void ValidateAuthorEdit(ColliderRow row)
        {
            if (Busy || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("当前不能修改逐项参数或审阅状态。");
            if (!Rows.Contains(row) || row.Review == null) throw new InvalidOperationException("资源列表已变化，请重新读取后操作。");
        }

        public void MarkPending(ColliderRow row)
        {
            ValidateAuthorEdit(row);
            var previous = row.Review.goals.ToArray();
            for (int i = 0; i < Profile.Targets.Length; i++)
                row.Review.Set(new ColliderReviewGoal { targetId = Profile.Targets[i].Id, decision = ColliderDecision.Pending,
                    model = row.ReviewGoal(i).model, current = row.ReviewGoal(i).current,
                    appliedAt = row.ReviewGoal(i).appliedAt, decidedAt = DateTime.UtcNow.ToString("O") });
            try { reviews.Save(); }
            catch { row.Review.goals = previous; throw; }
            Message = "已标为待处理，仅修改审阅状态；当前碰撞体和候选保持。";
            Notify();
        }

        public void SetGenerationSettings(ColliderRow row, string targetId, int targetCount, float silhouetteErrorPercent,
            int? maxHullVertices = null, float? pickingInwardErrorPercent = null, PickingShapeMode? pickingShapes = null,
            float? nativeShapeErrorPercent = null, float? nativeClickChangePercent = null) =>
            ChangeRecipe(row, targetId, targetCount, silhouetteErrorPercent, maxHullVertices, pickingInwardErrorPercent, pickingShapes, nativeShapeErrorPercent, nativeClickChangePercent);
        public void ResetGenerationSettings(ColliderRow row, string targetId) => ChangeRecipe(row, targetId, null, 0);
        void ChangeRecipe(ColliderRow row, string targetId, int? targetCount, float silhouetteErrorPercent,
            int? maxHullVertices = null, float? pickingInwardErrorPercent = null, PickingShapeMode? pickingShapes = null,
            float? nativeShapeErrorPercent = null, float? nativeClickChangePercent = null)
        {
            ValidateAuthorEdit(row);
            int index = Indices(ColliderTargetSelection.Only(targetId)).Single();
            var defaults = Profile.Targets[index].Settings;
            var old = row.Recipe(index);
            ColliderItemRecipe next = null;
            if (targetCount.HasValue)
            {
                var settings = row.EffectiveSettings(index); settings.TargetCount = targetCount.Value; settings.SilhouetteErrorPercent = silhouetteErrorPercent;
                if (maxHullVertices.HasValue) settings.MaxHullVertices = maxHullVertices.Value;
                if (pickingInwardErrorPercent.HasValue) settings.PickingInwardErrorPercent = pickingInwardErrorPercent.Value;
                if (pickingShapes.HasValue) settings.PickingShapes = pickingShapes.Value;
                if (nativeShapeErrorPercent.HasValue) settings.NativeShapeErrorPercent = nativeShapeErrorPercent.Value;
                if (nativeClickChangePercent.HasValue) settings.NativeClickChangePercent = nativeClickChangePercent.Value;
                settings.Validate();
                if (settings.Goal == ColliderGeometryGoal.Structure && settings.TargetCount > PickingProxy.GenerationSettings.ConvexCountLimit)
                    throw new ArgumentException("结构贴合的碰撞体数量上限为16。");
                bool meshOverride = defaults.Goal == ColliderGeometryGoal.Structure &&
                    (settings.MaxHullVertices != defaults.MaxHullVertices || settings.PickingInwardErrorPercent != defaults.PickingInwardErrorPercent);
                bool shapeOverride = defaults.Goal == ColliderGeometryGoal.Structure && settings.PickingShapes != defaults.PickingShapes;
                bool nativeOverride = defaults.Goal == ColliderGeometryGoal.Structure &&
                    (settings.NativeShapeErrorPercent != defaults.NativeShapeErrorPercent || settings.NativeClickChangePercent != defaults.NativeClickChangePercent);
                if (settings.TargetCount != defaults.TargetCount || settings.SilhouetteErrorPercent != defaults.SilhouetteErrorPercent || meshOverride || shapeOverride || nativeOverride)
                    next = new ColliderItemRecipe { targetId = targetId, goal = defaults.Goal, targetCount = settings.TargetCount, silhouetteErrorPercent = settings.SilhouetteErrorPercent,
                        overrideMeshBudget = meshOverride, maxHullVertices = meshOverride ? settings.MaxHullVertices : 0,
                        pickingInwardErrorPercent = meshOverride ? settings.PickingInwardErrorPercent : 0,
                        overridePickingShapes = shapeOverride, pickingShapes = shapeOverride ? settings.PickingShapes : default,
                        overrideNativeTolerance = nativeOverride, nativeShapeErrorPercent = nativeOverride ? settings.NativeShapeErrorPercent : 0,
                        nativeClickChangePercent = nativeOverride ? settings.NativeClickChangePercent : 0 };
            }
            if (old == null && next == null || old != null && next != null && old.targetCount == next.targetCount && old.silhouetteErrorPercent == next.silhouetteErrorPercent &&
                old.overrideMeshBudget == next.overrideMeshBudget && old.maxHullVertices == next.maxHullVertices && old.pickingInwardErrorPercent == next.pickingInwardErrorPercent &&
                old.overridePickingShapes == next.overridePickingShapes && old.pickingShapes == next.pickingShapes &&
                old.overrideNativeTolerance == next.overrideNativeTolerance && old.nativeShapeErrorPercent == next.nativeShapeErrorPercent && old.nativeClickChangePercent == next.nativeClickChangePercent) return;
            var previous = row.Review.recipes;
            row.Review.recipes = previous.Where(r => r.targetId != targetId || r.goal != defaults.Goal)
                .Concat(next == null ? Array.Empty<ColliderItemRecipe>() : new[] { next }).ToArray();
            try { reviews.Save(); }
            catch { row.Review.recipes = previous; throw; }
            Message = next == null ? "已恢复本栏默认参数；需要时重新生成预览。" : "已保存本Prefab本栏参数，请重新生成预览。";
            Notify();
        }

        static void SetDecision(ColliderRow row, int index, ColliderDecision decision)
        {
            row.Review.Set(new ColliderReviewGoal
            {
                targetId = row.Profile.Targets[index].Id, decision = decision, model = row.Inspection.ModelStamp, current = row.Inspection.Current[index].Stamp,
                decidedAt = DateTime.UtcNow.ToString("O"), appliedAt = row.ReviewGoal(index).appliedAt
            });
        }

        public bool CanReuseCurrentEnvelope(ColliderRow row, string targetId) =>
            !Busy && !EditorApplication.isPlayingOrWillChangePlaymode && Rows.Contains(row) && row.Review != null &&
            CurrentEnvelopeIssue(row,Indices(ColliderTargetSelection.Only(targetId)).Single()) == null;

        internal string CurrentEnvelopeIssue(ColliderRow row, int index)
        {
            if (Profile.Targets[index].Settings.Goal != ColliderGeometryGoal.Envelope)
                return "复用当前只用于整体包围候选。";
            if (!row.Inspection.Ready) return row.Inspection.Error;
            return ColliderGeneration.CurrentNativeSnapshotIssue(row.Inspection.Current[index].Shapes);
        }

        public void ReuseCurrentEnvelope(ColliderRow row, string targetId)
        {
            ValidateAuthorEdit(row);
            int index = Indices(ColliderTargetSelection.Only(targetId)).Single();
            RefreshCurrent(row);
            string issue = CurrentEnvelopeIssue(row,index);
            if (issue != null) throw new InvalidOperationException(issue);
            var revision = row.ObservedRevision;
            var root = PrefabUtility.LoadPrefabContents(row.Path);
            try
            {
                Adapter.Prepare(root,Profile,new[]{targetId});
                var layout = Adapter.Inspect(root,Profile);
                if (layout.Error != null) throw new InvalidOperationException(layout.Error);
                var preview = ColliderGeneration.CaptureCurrentNativePreview(Request(row,root,index),layout.Targets[index].Shapes);
                if (Revision(row.Path) != revision) throw new InvalidOperationException("读取期间资源发生变化，请刷新后重试。");
                SaveReusedCandidate(row,index,preview,root.transform.worldToLocalMatrix,"已复用当前整体包围");
                Message = $"已将 {row.Name} 的当前整体包围复制为本栏候选；未修改Prefab。";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Notify();
        }

        public bool CanReuseEnvelopePreview(ColliderRow row, string sourceId, string targetId) =>
            !Busy && !EditorApplication.isPlayingOrWillChangePlaymode && Rows.Contains(row) && row.Review != null &&
            ReuseEnvelopeIssue(row, Indices(ColliderTargetSelection.Only(sourceId)).Single(),
                Indices(ColliderTargetSelection.Only(targetId)).Single()) == null;

        internal string ReuseEnvelopeIssue(ColliderRow row, int source, int target)
        {
            if (Profile.Targets[source].Settings.Goal != ColliderGeometryGoal.Envelope ||
                Profile.Targets[target].Settings.Goal != ColliderGeometryGoal.Structure)
                return "只能将整体包围候选复用为结构贴合候选。";
            if (row.Candidates[source].Preview == null) return "整体包围候选缺失，请先生成或复用当前碰撞体。";
            var preview = row.Candidates[source].Preview;
            if (preview.SourceStamp != row.Inspection.SourceStamps[target])
                return "两栏的模型来源或拟合坐标不同，不能直接复用。";
            return null;
        }

        public void ReuseEnvelopePreview(ColliderRow row, string sourceId, string targetId)
        {
            ValidateAuthorEdit(row);
            int source = Indices(ColliderTargetSelection.Only(sourceId)).Single();
            int target = Indices(ColliderTargetSelection.Only(targetId)).Single();
            RefreshCurrent(row);
            string issue = ReuseEnvelopeIssue(row, source, target);
            if (issue != null) throw new InvalidOperationException(issue);
            var original = row.Candidates[source];
            // Clone detached shape data: only the destination's recipe/ownership changes.
            // No fitting or automatic open-space acceptance is implied by this manual choice.
            var preview = ColliderGeneration.CopyForReuse(original.Preview,row.EffectiveSettings(target));
            preview.Fit.GenerationSeconds = 0;
            preview.Fit.Note = $"手动复用{Profile.Targets[source].Name}候选 · {preview.Count}个碰撞体。\n" +
                "形状和数量原样暂存，不重新拟合或执行生成预算验收。";
            SaveReusedCandidate(row,target,preview,original.WorldToItem,$"已复用{Profile.Targets[source].Name}候选");
            Message = $"已将 {row.Name} 的{Profile.Targets[source].Name}复用到{Profile.Targets[target].Name}候选；未修改Prefab。";
            Notify();
        }

        static void SaveReusedCandidate(ColliderRow row, int index, ColliderGeneration.Preview preview, Matrix4x4 worldToItem, string stage)
        {
            var candidate = row.Candidates[index];
            candidate.Preview = preview; candidate.Revision = row.ObservedRevision;
            candidate.WorldToItem = worldToItem; candidate.SavedAt = DateTime.UtcNow.ToString("O");
            candidate.Error = null; candidate.Applied = false; candidate.Timer.Reset();
            candidate.Progress = ColliderProgress.Complete; candidate.Fraction = 1; candidate.Stage = stage;
            ColliderCache.Save(row);
        }

        public async Task GenerateAsync(ColliderRow[] rows, ColliderTargetSelection scope, bool rebuildPickingAnalysis = false)
        {
            if (Busy) throw new InvalidOperationException("已有生成任务正在运行。");
            var indices = Indices(scope).ToArray();
            foreach (var row in rows) RefreshCurrent(row);
            int skipped = rows.Count(r => !r.Inspection.Ready);
            rows = rows.Where(r => r.Inspection.Ready).ToArray();
            if (rows.Length == 0) { Message = $"没有可生成的物品，基础配置未就绪 {skipped} 项。"; Notify(); return; }
            Busy = true;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            int completed = 0, failed = 0;
            foreach (var row in rows)
                foreach (var index in indices)
                {
                    var candidate = row.Candidates[index];
                    candidate.Progress = ColliderProgress.Queued;
                    candidate.Stage = "等待前面的任务完成";
                    candidate.Timer.Reset();
                    candidate.Fraction = 0;
                }
            Notify();
            try
            {
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested();
                    row.Error = null;
                    Message = $"生成 {Array.IndexOf(rows, row) + 1}/{rows.Length}：{row.Name}（可取消）";
                    Notify();
                    GameObject root = null;
                    try
                    {
                        var revision = Revision(row.Path);
                        row.ObservedRevision = revision;
                        root = PrefabUtility.LoadPrefabContents(row.Path);
                        Adapter.Prepare(root, Profile, indices.Select(i => Profile.Targets[i].Id).ToArray());
                        var worldToItem = root.transform.worldToLocalMatrix;
                        foreach (var index in indices)
                        {
                            token.ThrowIfCancellationRequested();
                            var candidate = row.Candidates[index];
                            candidate.Error = null;
                            candidate.Applied = false;
                            candidate.Progress = ColliderProgress.Running;
                            candidate.Stage = "准备模型数据";
                            candidate.Fraction = 0;
                            candidate.Timer.Restart();
                            Notify();
                            var progress = new Progress<ColliderGenerationProgress>(update =>
                            {
                                if (candidate.Progress != ColliderProgress.Running) return;
                                candidate.Stage = update.Stage;
                                candidate.Fraction = update.Fraction;
                                // Visible rows already poll progress in Tick. Work-bar
                                // updates do not invalidate counts, queues or geometry.
                            });
                            try
                            {
                                var request = Request(row, root, index); request.RebuildPickingAnalysis = rebuildPickingAnalysis;
                                var preview = await ColliderGeneration.PreviewAsync(request, token, progress);
                                if (Revision(row.Path) != revision)
                                    throw new InvalidOperationException("生成期间资源发生变化，请重新生成。");
                                candidate.Preview = preview;
                                candidate.Revision = revision;
                                candidate.WorldToItem = worldToItem;
                                candidate.SavedAt = DateTime.UtcNow.ToString("O");
                                candidate.Progress = ColliderProgress.Complete;
                                candidate.Fraction = 1;
                                candidate.Stage = $"预览就绪 · {candidate.Preview.Count} 个碰撞体";
                                ColliderCache.Save(row);
                            }
                            catch (OperationCanceledException)
                            {
                                candidate.Progress = ColliderProgress.Cancelled;
                                candidate.Stage = "生成已取消";
                                throw;
                            }
                            catch (Exception error)
                            {
                                candidate.Error = error.Message; failed++;
                                candidate.Progress = ColliderProgress.Failed;
                                candidate.Stage = error.Message;
                                Debug.LogError($"{row.Path} · {Profile.Targets[index].Name}生成预览失败：\n{error}");
                            }
                            finally { candidate.Timer.Stop(); }
                            Notify();
                        }
                        completed++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error)
                    {
                        row.Error = error.Message; failed++;
                        Debug.LogError($"{row.Path} · 准备预览失败：\n{error}");
                        foreach (var index in indices)
                            if (row.Candidates[index].Progress == ColliderProgress.Queued)
                            {
                                row.Candidates[index].Progress = ColliderProgress.Failed;
                                row.Candidates[index].Stage = error.Message;
                            }
                    }
                    finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
                }
                Message = $"预览处理 {completed}/{rows.Length} 个物品，失败 {failed} 项，基础配置未就绪跳过 {skipped} 项。尚未写回 Prefab。";
            }
            catch (OperationCanceledException) { Message = "已取消生成，已完成预览保留，Prefab 未修改。"; }
            finally
            {
                foreach (var row in rows)
                    foreach (var candidate in row.Candidates)
                        if (candidate.Progress == ColliderProgress.Queued)
                        {
                            candidate.Progress = ColliderProgress.Cancelled;
                            candidate.Stage = "等待已取消";
                        }
                cancellation.Dispose();
                cancellation = null;
                Busy = false;
                Notify();
            }
        }

        public void Apply(ColliderRow[] rows, ColliderTargetSelection scope)
        {
            if (Busy) throw new InvalidOperationException("生成期间不能应用碰撞体。");
            int skipped = rows.Count(r => r.CandidateIndices(scope).Length == 0);
            rows = rows.Where(r => r.CandidateIndices(scope).Length > 0).ToArray();
            int completed = 0, failed = 0, processed = 0;
            try
            {
                foreach (var row in rows)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("应用碰撞体", row.Name,
                        (float)processed / rows.Length)) break;
                    try
                    {
                        ApplyRow(row, scope);
                        try { reviews.Save(); }
                        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                        { throw new IOException("Prefab已保存，但审阅清单保存失败；请修复项目数据写入权限后刷新。", error); }
                        ColliderCache.Save(row); completed++;
                    }
                    catch (Exception error) { row.Error = error.Message; failed++; Debug.LogError($"{row.Path}：{error.Message}"); }
                    processed++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Message = $"已应用 {completed}，失败 {failed}，无候选跳过 {skipped}，未处理 {rows.Length - processed}。已完成项已保存。";
            Notify();
        }

        internal static void ApplyRow(ColliderRow row, ColliderTargetSelection scope)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出播放模式。");
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == row.Path)
                throw new InvalidOperationException("请先关闭该物品的 Prefab 模式，再从面板应用，避免覆盖编辑中的内容。");
            if (!AssetDatabase.IsOpenForEdit(row.Path)) throw new IOException("Prefab 不可写。");
            var indices = row.CandidateIndices(scope);
            if (indices.Length == 0) throw new InvalidOperationException("所选范围没有候选，请先生成预览；未修改Prefab。");
            var observed = indices.Select(i => row.Inspection.Current[i].Stamp).ToArray();
            RefreshCurrent(row);
            if (indices.Any(i => !row.CanApply(i))) throw new InvalidOperationException("候选已过期或配置未就绪，请重新生成或整理归属；整个Prefab未修改。");
            if (indices.Where((i, slot) => observed[slot] != row.Inspection.Current[i].Stamp).Any())
                throw new InvalidOperationException("当前碰撞体刚发生变化，请查看刷新后的结果再应用；整个Prefab未修改。");
            var valid = Enumerable.Range(0, row.Profile.Targets.Length).Where(row.IsCurrent).ToArray();
            var root = PrefabUtility.LoadPrefabContents(row.Path);
            try
            {
                row.Owner.Adapter.Prepare(root, row.Profile, indices.Select(i => row.Profile.Targets[i].Id).ToArray());
                // Validate both results before touching either output. A single Prefab
                // is saved once, after every selected group and all bindings succeed.
                var previews = indices.Select(i => ColliderGeneration.RetargetPreview(row.Candidates[i].Preview, Request(row, root, i))).ToArray();
                foreach (var preview in previews) ColliderGeneration.ApplyPreview(preview);
                if (PrefabUtility.SaveAsPrefabAsset(root, row.Path) == null) throw new IOException("保存 Prefab 失败。");
                RefreshCurrent(row);
                foreach (int index in valid) row.Candidates[index].Revision = row.ObservedRevision;
                row.Error = null;
                foreach (var index in indices)
                {
                    row.Candidates[index].Applied = true;
                    row.ReviewGoal(index).appliedAt = DateTime.UtcNow.ToString("O");
                }
                ColliderReviews.CompleteRegistration(row);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        internal static string Revision(string path) => AssetDatabase.GetAssetDependencyHash(path).ToString();

        static ColliderGenerationRequest Request(ColliderRow row, GameObject root, int index)
        {
            var request = ColliderAuthoringContext.BuildRequest(row.Owner.Adapter, root, row.Profile, row.Profile.Targets[index].Id);
            request.Settings = row.EffectiveSettings(index);
            return request;
        }

        public void Cancel() => cancellation?.Cancel();
        void Notify() { if (!disposed) Changed?.Invoke(); }
        public void Dispose() { disposed = true; Cancel(); }
    }
}
