using System;
using System.Collections.Generic;
using System.Linq;
using July.Release;
using UnityEditor;

namespace July.Release.Editor
{
    /// <summary>
    /// 面板间共享状态。由 BuildToolWindow 创建，传给所有 <see cref="IBuildToolPanel"/>。
    /// 每个面板只读/写自己负责的字段，通过此对象实现跨面板通信。
    /// </summary>
    public sealed class BuildToolContext
    {
        // ── 共享配置 ──
        public IReleaseBootConfig BootConfig;
        public IReleaseBuildConfig BuildConfig;

        // ── Platform（PlatformPanel 写 EditorPlatformPref.Platform，其余读）──
        public readonly BuildToolSelection Selection = new();
        public string[] BackupVersions = Array.Empty<string>();
        string _backupScope;
        public BuildContext Preview;
        public BuildContext LastBuild;
        public July.Build.BuildResult LastResult;

        public void SyncConfiguration()
        {
            BootConfig = ReleaseProject.LoadBootConfig();
            BuildConfig = ReleaseProject.LoadBuildConfig();
            var scope = EditorUserBuildSettings.activeBuildTarget + ":" + CurrentPlatform;
            if (_backupScope != scope)
            {
                _backupScope = scope;
                Selection.AotBaseline = ProjectEditorPrefs.GetString("BuildTool_Baseline:" + scope, "");
                RefreshBackups();
            }
        }

        public void RefreshBackups()
        {
            BackupVersions = HybridCLRBuildHelper.GetAvailableBackupVersions(
                EditorUserBuildSettings.activeBuildTarget, CurrentPlatform);
            if (!BackupVersions.Contains(Selection.AotBaseline))
                SelectBaseline(BackupVersions.FirstOrDefault());
        }

        public void SelectBaseline(string version)
        {
            Selection.AotBaseline = version;
            ProjectEditorPrefs.SetString("BuildTool_Baseline:" + _backupScope, version ?? "");
        }

        public BuildContext CreatePreview()
        {
            var context = new BuildContext
            {
                Target = EditorUserBuildSettings.activeBuildTarget,
                Platform = CurrentPlatform, Env = BootConfig.EnvName,
                PlanVersion = BuildConfig.planVersion, Development = DebugBuild,
                CloudUrl = BuildConfig.cloudUrl, CdnUrl = BootConfig.cdnUrl,
            };
            Selection.ApplyTo(context);
            return Preview = context;
        }

        public bool DebugBuild;
        public string CurrentPlatform => EditorPlatformPref.Platform;

        // ── 构建结果（BuildPipelinePanel 写，VersionPanel/DiffPanel 读）──
        public string LastPackageVersion;
        public DiffResult LastDiff;

        // ── 状态栏（任意面板可写）──
        public string StatusMessage;
        public MessageType StatusType;
        public Action Repaint;

        public void SetStatus(string msg, MessageType type)
        {
            StatusMessage = msg;
            StatusType = type;
            Repaint?.Invoke();
        }
    }

    /// <summary>
    /// 构建前后文件差异结果（不可变）。
    /// </summary>
    public sealed class DiffResult
    {
        public enum DiffType { Added, Modified, Removed }

        public struct Entry
        {
            public string Name;
            public DiffType Type;
            public long OldSize;
            public long NewSize;
        }

        public List<Entry> Entries { get; }
        public int UnchangedCount { get; }
        public long TotalOld { get; }
        public long TotalNew { get; }

        DiffResult(List<Entry> entries, int unchanged, long totalOld, long totalNew)
        {
            Entries = entries;
            UnchangedCount = unchanged;
            TotalOld = totalOld;
            TotalNew = totalNew;
        }

        public static DiffResult Compute(Dictionary<string, long> before, Dictionary<string, long> after)
        {
            var entries = new List<Entry>();
            var unchanged = 0;

            foreach (var kvp in after)
            {
                if (!before.TryGetValue(kvp.Key, out var oldSize))
                    entries.Add(new Entry { Name = kvp.Key, Type = DiffType.Added, NewSize = kvp.Value });
                else if (oldSize != kvp.Value)
                    entries.Add(new Entry
                        { Name = kvp.Key, Type = DiffType.Modified, OldSize = oldSize, NewSize = kvp.Value });
                else
                    unchanged++;
            }

            foreach (var kvp in before)
            {
                if (!after.ContainsKey(kvp.Key))
                    entries.Add(new Entry { Name = kvp.Key, Type = DiffType.Removed, OldSize = kvp.Value });
            }

            entries = entries.OrderBy(e => e.Type).ThenBy(e => e.Name).ToList();
            return new DiffResult(entries, unchanged, before.Values.Sum(), after.Values.Sum());
        }
    }
}
