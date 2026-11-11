using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    [Serializable]
    public sealed class ColliderOutputTarget
    {
        public string Id, Name, Path;
        public ColliderGenerationSettings Settings = new ColliderGenerationSettings();
        public ColliderGenerationSettings AlternateSettings;
        public bool FixedGoal;
        public ColliderOutputTarget(string id, string name, string path, ColliderGeometryGoal goal, bool fixedGoal = false)
        { Id = id; Name = name; Path = path; Settings = ColliderGenerationSettings.ForGoal(goal); FixedGoal = fixedGoal;
            AlternateSettings = ColliderGenerationSettings.ForGoal(goal == ColliderGeometryGoal.Envelope ? ColliderGeometryGoal.Structure : ColliderGeometryGoal.Envelope); }
        public void ChangeGoal(ColliderGeometryGoal goal)
        {
            if (FixedGoal) throw new InvalidOperationException("项目已固定该目标的生成方式。");
            if (Settings.Goal == goal) return;
            var previous = Settings; Settings = AlternateSettings ?? ColliderGenerationSettings.ForGoal(goal); AlternateSettings = previous;
        }
    }

    public sealed class ColliderAuthoringProfile
    {
        [Serializable] sealed class TargetCopy { public ColliderOutputTarget[] targets; }
        public readonly string Id, Title;
        public readonly bool EditableTargets;
        public string MeshDirectory;
        public string SourcePath = "";
        public ColliderOutputTarget[] Targets;
        public ColliderPerformanceSettings Performance = new ColliderPerformanceSettings();
        public string CacheDirectory => "July/ColliderAuthoring";
        public string ReviewFile => "ProjectSettings/July/ColliderAuthoring/" + Id + ".reviews.json";
        public ColliderAuthoringProfile(string id, string title, ColliderOutputTarget[] targets,
            string meshDirectory = "Assets/Generated/ColliderAuthoring", bool editableTargets = true)
        { Id = id; Title = title; Targets = targets; MeshDirectory = meshDirectory; EditableTargets = editableTargets; }
        internal ColliderAuthoringProfile Copy() => new ColliderAuthoringProfile(Id, Title,
            JsonUtility.FromJson<TargetCopy>(JsonUtility.ToJson(new TargetCopy { targets = Targets })).targets, MeshDirectory, EditableTargets)
        {
            SourcePath = SourcePath, Performance = Performance.Copy()
        };

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || Id.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
                throw new ArgumentException("制作配置标识无效。");
            ValidateAssetDirectory(MeshDirectory);
            Performance.Validate();
            if (Targets == null || Targets.Length == 0 || Targets.Any(t => t == null || string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Name)))
                throw new ArgumentException("至少需要一个有效的输出目标。");
            if (Targets.Select(t => t.Id).Distinct().Count() != Targets.Length) throw new ArgumentException("输出目标标识重复。");
            foreach (var target in Targets)
            {
                ValidateNodePath(target.Path); target.Settings.Validate();
                foreach (var other in Targets.Where(t => t != target))
                    if (target.Path == other.Path || target.Path.StartsWith(other.Path + "/", StringComparison.Ordinal))
                        throw new ArgumentException("输出目标不能重复或互相包含。");
            }
            if (SourcePath.Length > 0) ValidateNodePath(SourcePath);
        }
        internal static void ValidateNodePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains('\\') || path.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new ArgumentException("节点须使用Prefab内的相对路径。");
        }
        internal static void ValidateAssetDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Contains('\\') || path.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new ArgumentException("生成Mesh目录须位于项目Assets内。");
        }
    }

    public readonly struct ColliderTargetSelection : IEquatable<ColliderTargetSelection>
    {
        public readonly string TargetId;
        public static ColliderTargetSelection All => default;
        public static ColliderTargetSelection Only(string targetId) => new ColliderTargetSelection(targetId);
        ColliderTargetSelection(string targetId) { TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId)); }
        public bool Equals(ColliderTargetSelection other) => TargetId == other.TargetId;
        public override bool Equals(object obj) => obj is ColliderTargetSelection other && Equals(other);
        public override int GetHashCode() => TargetId?.GetHashCode() ?? 0;
        public static bool operator ==(ColliderTargetSelection a, ColliderTargetSelection b) => a.Equals(b);
        public static bool operator !=(ColliderTargetSelection a, ColliderTargetSelection b) => !a.Equals(b);
    }

    // Project configuration is independent of candidate caches and review history.
    internal static class ColliderAuthoringSettings
    {
        internal const string FileName = "ProjectSettings/July/ColliderAuthoring/settings.json";
        [Serializable] sealed class Document { public int version = 1; public List<Entry> profiles = new List<Entry>(); }
        [Serializable] sealed class Entry { public string id, meshDirectory, sourcePath; public ColliderOutputTarget[] targets; public ColliderPerformanceSettings performance; }
        [Serializable] sealed class ViewMode { public string targetId; public ColliderGeometryGoal goal; }
        [Serializable] sealed class ViewState { public ViewMode[] modes; }
        static string ViewFile(ColliderAuthoringProfile profile) => "Library/July/ColliderAuthoring/" + profile.Id + "/view.json";
        static Document Read()
        {
            var data = File.Exists(FileName) ? JsonUtility.FromJson<Document>(AuthoringFile.ReadText(FileName)) : new Document();
            if (data == null || data.version != 1 || data.profiles == null || data.profiles.Any(p => p == null) || data.profiles.Select(p => p.id).Distinct().Count() != data.profiles.Count)
                throw new InvalidDataException("碰撞体制作配置格式无效。");
            return data;
        }
        internal static void Load(ColliderAuthoringProfile profile)
        {
            var entry = Read().profiles.SingleOrDefault(p => p.id == profile.Id);
            if (entry != null)
            {
                profile.MeshDirectory = entry.meshDirectory;
                // Earlier configuration files have no performance field.
                profile.Performance = entry.performance ?? profile.Performance;
                if (profile.EditableTargets) { profile.SourcePath = entry.sourcePath ?? ""; profile.Targets = entry.targets; }
            }
            profile.Validate();
            string viewFile = ViewFile(profile);
            if (File.Exists(viewFile))
            {
                var state = JsonUtility.FromJson<ViewState>(AuthoringFile.ReadText(viewFile));
                if (state?.modes == null || state.modes.Any(m => m == null || !Enum.IsDefined(typeof(ColliderGeometryGoal), m.goal)))
                    throw new InvalidDataException("碰撞体面板状态无效：" + viewFile);
                foreach (var mode in state.modes)
                {
                    var target = profile.Targets.SingleOrDefault(t => t.Id == mode.targetId);
                    if (target != null && !target.FixedGoal) target.ChangeGoal(mode.goal);
                }
            }
        }
        internal static void SaveView(ColliderAuthoringProfile profile)
        {
            AuthoringFile.WriteText(ViewFile(profile), JsonUtility.ToJson(new ViewState { modes = profile.Targets.Where(t => !t.FixedGoal)
                .Select(t => new ViewMode { targetId = t.Id, goal = t.Settings.Goal }).ToArray() }));
        }
        internal static void Save(ColliderAuthoringProfile profile)
        {
            profile.Validate(); var data = Read(); data.profiles.RemoveAll(p => p.id == profile.Id);
            data.profiles.Add(new Entry { id = profile.Id, meshDirectory = profile.MeshDirectory, sourcePath = profile.SourcePath, targets = profile.Targets, performance = profile.Performance });
            AuthoringFile.WriteText(FileName, JsonUtility.ToJson(data, true));
        }
    }
}
