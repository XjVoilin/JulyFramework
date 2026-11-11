using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace July.Physics.Authoring
{
    public enum ColliderDecision { Pending = 0, Keep = 1, Manual = 3 }
    internal enum ColliderQueue { All, New, Recheck, Issues, Pending, Done }
    [Serializable] internal sealed class ColliderItemRecipe
    {
        public string targetId;
        public ColliderGeometryGoal goal;
        public int targetCount;
        public float silhouetteErrorPercent;
        public bool overrideMeshBudget;
        public int maxHullVertices;
        public float pickingInwardErrorPercent;
        public bool overridePickingShapes;
        public PickingShapeMode pickingShapes;
        public bool overrideNativeTolerance;
        public float nativeShapeErrorPercent, nativeClickChangePercent;
    }
    [Serializable] internal sealed class ColliderReviewGoal
    {
        public string targetId;
        public ColliderDecision decision;
        public string model, current, decidedAt, appliedAt;
    }
    [Serializable] internal sealed class ColliderReviewRecord
    {
        public string guid, firstSeenAt;
        public bool isNew;
        public ColliderReviewGoal[] goals;
        public ColliderItemRecipe[] recipes = Array.Empty<ColliderItemRecipe>();
        internal ColliderReviewGoal Goal(string id) => goals.Single(g => g.targetId == id);
        internal void Set(ColliderReviewGoal value) { goals[Array.FindIndex(goals, g => g.targetId == value.targetId)] = value; }
    }
    internal sealed class ColliderReviews
    {
        [Serializable] sealed class Document { public int version = 2; public List<ColliderReviewRecord> records = new List<ColliderReviewRecord>(); }
        readonly string path;
        readonly Document document;
        readonly Dictionary<string, ColliderReviewRecord> records;
        readonly ColliderAuthoringProfile profile;
        internal bool Dirty { get; private set; }
        internal ColliderReviews(ColliderAuthoringProfile profile, string overridePath = null)
        {
            this.profile = profile;
            path = overridePath ?? profile.ReviewFile;
            document = File.Exists(path) ? JsonUtility.FromJson<Document>(AuthoringFile.ReadText(path)) : new Document();
            if (document == null || document.version != 2 || document.records == null ||
                document.records.Any(r => r == null || !Guid.TryParseExact(r.guid, "N", out _) || r.goals == null ||
                    r.goals.Any(g => g == null || !Enum.IsDefined(typeof(ColliderDecision), g.decision))))
                throw new InvalidDataException("审阅清单格式无效，请修复作者数据，未自动重建。");
            if (document.records.Select(r => r.guid).Distinct().Count() != document.records.Count) throw new InvalidDataException("审阅清单含重复GUID。");
            if (document.records.Any(r => r.goals.Any(g => string.IsNullOrWhiteSpace(g.targetId)) || r.goals.Select(g => g.targetId).Distinct().Count() != r.goals.Length))
                throw new InvalidDataException("审阅目标标识无效或重复。");
            foreach (var record in document.records)
            {
                // Existing records without overrides inherit their profile defaults.
                record.recipes ??= Array.Empty<ColliderItemRecipe>();
                if (record.recipes.Any(r => r == null || string.IsNullOrWhiteSpace(r.targetId) || !Enum.IsDefined(typeof(ColliderGeometryGoal), r.goal)) ||
                    record.recipes.Select(r => r.targetId + ":" + (int)r.goal).Distinct().Count() != record.recipes.Length)
                    throw new InvalidDataException("逐项参数目标无效或重复。");
                foreach (var recipe in record.recipes)
                {
                    try
                    {
                        var settings = new ColliderGenerationSettings { Goal = recipe.goal, TargetCount = recipe.targetCount, SilhouetteErrorPercent = recipe.silhouetteErrorPercent };
                        if (recipe.overrideMeshBudget) { settings.MaxHullVertices = recipe.maxHullVertices; settings.PickingInwardErrorPercent = recipe.pickingInwardErrorPercent; }
                        if (recipe.overridePickingShapes) settings.PickingShapes = recipe.pickingShapes;
                        if (recipe.overrideNativeTolerance) { settings.NativeShapeErrorPercent = recipe.nativeShapeErrorPercent; settings.NativeClickChangePercent = recipe.nativeClickChangePercent; }
                        settings.Validate();
                    }
                    catch (ArgumentException error) { throw new InvalidDataException("逐项生成参数无效。", error); }
                }
            }
            records = document.records.ToDictionary(r => r.guid, StringComparer.Ordinal);

        }
        internal bool Register(IEnumerable<ColliderRow> rows)
        {
            bool changed = false;
            foreach (var row in rows)
            {
                if (!records.TryGetValue(row.Guid, out var record))
                {
                    record = new ColliderReviewRecord { guid = row.Guid, isNew = !row.HasCacheHistory, firstSeenAt = DateTime.UtcNow.ToString("O"), goals = Array.Empty<ColliderReviewGoal>() };
                    records.Add(row.Guid, record); document.records.Add(record); changed = true;
                }
                foreach (var target in profile.Targets)
                    if (!record.goals.Any(g => g.targetId == target.Id))
                    { record.goals = record.goals.Concat(new[] { new ColliderReviewGoal { targetId = target.Id } }).ToArray(); changed = true; }
                row.Review = record; changed |= CompleteRegistration(row);
            }
            if (changed) Dirty = true;
            return changed;
        }
        internal static bool CompleteRegistration(ColliderRow row)
        {
            if (!row.Review.isNew || !row.Confirmed) return false;
            row.Review.isNew = false; return true;
        }
        internal void Save()
        {
            Dirty = true; document.version = 2;
            document.records.Sort((a, b) => string.CompareOrdinal(a.guid, b.guid));
            AuthoringFile.WriteText(path,JsonUtility.ToJson(document,true));
            Dirty = false;
        }
    }
}
