using System;
using System.Collections.Generic;
using System.Linq;

namespace July.AssetCheck
{
    public sealed class AssetCheckIssue
    {
        public string AssetPath { get; }
        public string TargetId { get; }
        public AssetCheckKind Kind { get; }
        public string RuleId { get; }
        public string Problem { get; }
        public string ResourceName { get; }
        public string Actual { get; }
        public string Expected { get; }
        public bool CanFix { get; }
        public bool Unverified { get; }
        public bool HasProblem { get; }
        public long LocalId { get; }
        public IReadOnlyList<string> Targets { get; }
        public string Location { get; }
        public IReadOnlyList<string> UnverifiedTargets { get; }

        public AssetCheckIssue(string assetPath, string guid, AssetCheckKind kind, string ruleId,
            string problem, string actual, string expected, bool canFix, bool unverified = false,
            string resourceName = null, long localId = 0, IReadOnlyList<string> targets = null, string location = "",
            bool? hasProblem = null, string propertyPath = null, IReadOnlyList<string> unverifiedTargets = null)
        {
            AssetPath = assetPath;
            TargetId = guid;
            Kind = kind;
            RuleId = ruleId;
            Problem = problem;
            Actual = actual;
            Expected = expected;
            CanFix = canFix;
            Unverified = unverified;
            HasProblem = hasProblem ?? !unverified;
            ResourceName = resourceName ?? assetPath;
            LocalId = localId;
            Targets = targets ?? Array.Empty<string>();
            Location = location;
            PropertyPath = propertyPath;
            UnverifiedTargets = unverifiedTargets ?? Array.Empty<string>();
        }

        internal AssetCheckObjectLocation ObjectLocation { get; set; }
        internal object Rule { get; set; }
        public string PropertyPath { get; }

        internal AssetCheckIssue ForTargets(IReadOnlyList<string> targets, bool unverified, bool hasProblem) =>
            new AssetCheckIssue(AssetPath, TargetId, Kind, RuleId, Problem, Actual, Expected, CanFix, unverified,
                ResourceName, LocalId, targets, Location, hasProblem, PropertyPath, unverified ? targets : Array.Empty<string>())
                { ObjectLocation = ObjectLocation, Rule = Rule };

        internal AssetCheckIssue ConfirmedFix()
        {
            if (!CanFix || !HasProblem) return null;
            return UnverifiedTargets.Count == 0 ? this : ForTargets(Targets.Except(UnverifiedTargets).ToArray(), false, true);
        }

        internal AssetCheckIssue UnverifiedFix() => !CanFix || UnverifiedTargets.Count == 0 ? null :
            ForTargets(UnverifiedTargets, true, false);

        internal string Key => RuleId + ":" + LocalId + ":" + Location;
    }

    public sealed class AssetCheckReport
    {
        public AssetCheckKind Kind { get; }
        public string Context { get; internal set; }
        public AssetCheckPlatform Platform { get; }
        public string Scope { get; }
        public DateTime StartedAt { get; }
        public bool IsSnapshot { get; internal set; }
        internal int SnapshotCheckedFiles, SnapshotIgnoredFiles;
        public int CheckedFiles => IsSnapshot ? SnapshotCheckedFiles : CheckedTargets.Count;
        public int IgnoredFiles => IsSnapshot ? SnapshotIgnoredFiles : IgnoredTargets.Count;
        public int AttentionFiles => Issues.Select(issue => issue.TargetId).Distinct().Count();
        public int UnverifiedOnlyFiles => AttentionFiles - ProblemAssets;
        public bool Complete { get; internal set; }
        public bool Cancelled { get; internal set; }
        public bool Stale { get; internal set; }
        public string Failure { get; internal set; }
        public int TotalFiles { get; internal set; }
        public List<string> CheckedTargets { get; } = new List<string>();
        public List<string> IgnoredTargets { get; } = new List<string>();
        public List<AssetCheckIssue> Issues { get; } = new List<AssetCheckIssue>();
        public int ProblemAssets => Issues.Where(issue => issue.HasProblem).Select(issue => issue.TargetId).Distinct().Count();
        public int UnverifiedAssets => Issues.Where(issue => issue.Unverified).Select(issue => issue.TargetId).Distinct().Count();
        public int PassedAssets => CheckedFiles - Issues.Select(issue => issue.TargetId).Distinct().Count();
        internal readonly HashSet<string> InputPaths = new HashSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> CollectedPaths = new HashSet<string>(StringComparer.Ordinal);
        internal bool CollectionReady;
        public int Revision { get; internal set; }

        internal AssetCheckReport(AssetCheckKind kind, string context, AssetCheckPlatform platform, string scope, DateTime? startedAt = null)
        {
            Kind = kind;
            Context = context;
            Platform = platform;
            Scope = scope;
            StartedAt = startedAt ?? DateTime.Now;
        }

        internal void Exclude(string guid)
        {
            var hadIssue = Issues.Any(issue => issue.TargetId == guid);
            Issues.RemoveAll(issue => issue.TargetId == guid);
            if (IsSnapshot && hadIssue)
            {
                SnapshotCheckedFiles--;
                SnapshotIgnoredFiles++;
            }
            if (CheckedTargets.Remove(guid) && !IgnoredTargets.Contains(guid))
                IgnoredTargets.Add(guid);
            Revision++;
        }
    }

    public sealed class AssetCheckFixSummary
    {
        public int ModifiedFiles { get; internal set; }
        public int SkippedFiles { get; internal set; }
        public bool Rechecked { get; internal set; }
        public bool Cancelled { get; internal set; }
    }

    internal interface IAssetCheckModule
    {
        AssetCheckKind Kind { get; }
        string RulesFingerprint { get; }
        string[] Collect(string scope);
        List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards);
        void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards);
    }

    internal interface IIncrementalAssetCheckModule
    {
        IEnumerable<bool> CheckIncrementally(string path, PlatformStandards standards, List<AssetCheckIssue> issues);
    }
}
