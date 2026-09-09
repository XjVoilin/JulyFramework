using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace July.Release.Editor
{
    /// <summary>
    /// AB 收集器自动同步工具：管理 Buildin / Lobby 静态分组 + 小游戏动态分组。
    /// 小游戏分组根据 Assets/Game/MiniGames 下的子目录自动增删，无需手动维护。
    /// </summary>
    public static class MiniGameCollectorHelper
    {
        private static string CollectorSettingPath => ReleaseProject.Profile.CollectorSettingPath;
        private static string MiniGamesRoot => ReleaseProject.Profile.MiniGamesRoot;
        private static string ResRoot => ReleaseProject.Profile.ResourcesRoot;

        private const string BuildinGroupName = "Buildin";
        private const string LobbyGroupName = "Lobby";
        private const string BuildinTag = "Buildin";
        private const string LobbyTag = "Lobby";

        private static readonly HashSet<string> ExcludedDirs = new() { "Base" };

        private static readonly HashSet<string> ProtectedGroupNames = new()
        {
            BuildinGroupName, LobbyGroupName,
            "HotFix", "AOTMeta", "Default Group"
        };

        [MenuItem("JulyGF/资源管理/同步 AB 分组（Buildin + Lobby + 小游戏）", priority = 60)]
        public static void SyncAll()
        {
            var setting = LoadSetting();
            if (setting == null) return;

            var package = GetDefaultPackage(setting);
            if (package == null) return;

            var changed = false;
            changed |= EnsureStaticGroups(package);
            changed |= SyncMiniGameGroups(package);

            if (changed)
            {
                EditorUtility.SetDirty(setting);
                AssetDatabase.SaveAssets();
                Debug.Log("[MiniGameCollector] AB 分组同步完成，已保存");
            }
            else
            {
                Debug.Log("[MiniGameCollector] AB 分组已是最新，无需变更");
            }
        }

        /// <summary>
        /// 供构建流水线预检调用：同步分组并返回是否就绪。
        /// </summary>
        public static bool ValidateAndSync()
        {
            SyncAll();

            var setting = LoadSetting();
            if (setting == null) return false;

            var package = GetDefaultPackage(setting);
            if (package == null) return false;

            if (!package.Groups.Any(g => g.GroupName == BuildinGroupName))
            {
                Debug.LogError("[MiniGameCollector] 缺少 Buildin 分组");
                return false;
            }

            if (!package.Groups.Any(g => g.GroupName == LobbyGroupName))
            {
                Debug.LogError("[MiniGameCollector] 缺少 Lobby 分组");
                return false;
            }

            return true;
        }

        #region Static Groups (Buildin + Lobby)

        private static bool EnsureStaticGroups(AssetBundleCollectorPackage package)
        {
            var changed = false;
            changed |= EnsureBuildinGroup(package);
            changed |= EnsureLobbyGroup(package);
            return changed;
        }

        private static bool EnsureBuildinGroup(AssetBundleCollectorPackage package)
        {
            if (package.Groups.Any(g => g.GroupName == BuildinGroupName))
                return false;

            var group = CreateGroup(BuildinGroupName, "首包启动资源（Launch 场景）", BuildinTag);
            group.Collectors.Add(CreateCollector(
                $"{ResRoot}/Scenes/Launch.unity", BuildinTag,
                nameof(PackRawFile), nameof(CollectScene)));

            package.Groups.Insert(0, group);
            Debug.Log($"[MiniGameCollector] 创建分组: {BuildinGroupName}");
            return true;
        }

        private static bool EnsureLobbyGroup(AssetBundleCollectorPackage package)
        {
            if (package.Groups.Any(g => g.GroupName == LobbyGroupName))
                return false;

            var group = CreateGroup(LobbyGroupName, "大厅资源（启动后下载）", LobbyTag);

            group.Collectors.Add(CreateCollector(
                $"{ResRoot}/Scenes/Lobby.unity", LobbyTag,
                nameof(PackRawFile), nameof(CollectScene)));
            group.Collectors.Add(CreateCollector(
                $"{ResRoot}/Configs", LobbyTag,
                nameof(PackCollector), nameof(CollectAll)));
            group.Collectors.Add(CreateCollector(
                $"{ResRoot}/Prefabs", LobbyTag,
                nameof(PackCollector), nameof(CollectAll)));
            group.Collectors.Add(CreateCollector(
                $"{ResRoot}/Sounds", LobbyTag,
                nameof(PackCollector), nameof(CollectAll)));
            group.Collectors.Add(CreateCollector(
                $"{ResRoot}/Texture", LobbyTag,
                nameof(PackCollector), nameof(CollectAll)));

            var insertIdx = package.Groups.FindIndex(g => g.GroupName == BuildinGroupName);
            package.Groups.Insert(insertIdx < 0 ? 0 : insertIdx + 1, group);
            Debug.Log($"[MiniGameCollector] 创建分组: {LobbyGroupName}");
            return true;
        }

        #endregion

        #region MiniGame Groups (Auto Sync)

        private static bool SyncMiniGameGroups(AssetBundleCollectorPackage package)
        {
            var gameDirs = ScanMiniGameDirs();
            var changed = false;

            foreach (var dirName in gameDirs)
            {
                var gamePath = $"{MiniGamesRoot}/{dirName}";
                var expected = BuildMiniGameCollectors(gamePath, dirName);
                if (expected.Count == 0)
                {
                    Debug.LogWarning($"[MiniGameCollector] 小游戏 {dirName} 未发现 Scenes/Scene 或 Res 目录，跳过收集");
                    continue;
                }

                var group = package.Groups.FirstOrDefault(g => g.GroupName == dirName);
                if (group == null)
                {
                    group = CreateGroup(dirName, $"小游戏 {dirName}（按需下载）", dirName);
                    group.Collectors.AddRange(expected);
                    package.Groups.Add(group);
                    Debug.Log($"[MiniGameCollector] 新增小游戏分组: {dirName}");
                    changed = true;
                }
            }

            var toRemove = new List<AssetBundleCollectorGroup>();
            foreach (var group in package.Groups)
            {
                if (ProtectedGroupNames.Contains(group.GroupName))
                    continue;

                if (IsMiniGameGroup(group.GroupName) && !gameDirs.Contains(group.GroupName))
                    toRemove.Add(group);
            }

            foreach (var group in toRemove)
            {
                package.Groups.Remove(group);
                Debug.Log($"[MiniGameCollector] 移除已失效的小游戏分组: {group.GroupName}");
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 构造小游戏分组期望的 collector 列表。
        /// 仅为尚未配置的小游戏创建默认 Collector。已存在的分组以 YooAsset 面板配置为准。
        /// 默认配置：场景使用 PackDirectory，Res 使用 PackCollector。
        /// 其余资源（Art/Audio/Texture 等 Res 外的目录）仅作为 prefab/场景的依赖资源自动进包。
        /// </summary>
        private static List<AssetBundleCollector> BuildMiniGameCollectors(string gamePath, string tag)
        {
            var list = new List<AssetBundleCollector>();

            if (HasAnyScene(gamePath))
            {
                list.Add(CreateCollector(
                    gamePath, tag,
                    nameof(PackDirectory), nameof(CollectScene)));
            }

            var resPath = $"{gamePath}/Res";
            if (Directory.Exists(resPath))
            {
                list.Add(CreateCollector(
                    resPath, tag,
                    nameof(PackCollector), nameof(CollectAll)));
            }

            return list;
        }

        private static bool HasAnyScene(string gamePath)
        {
            return Directory.Exists(gamePath)
                   && Directory.EnumerateFiles(gamePath, "*.unity", SearchOption.AllDirectories).Any();
        }

        private static HashSet<string> ScanMiniGameDirs()
        {
            var result = new HashSet<string>();
            if (!Directory.Exists(MiniGamesRoot))
                return result;

            foreach (var fullPath in Directory.GetDirectories(MiniGamesRoot))
            {
                var dirName = Path.GetFileName(fullPath);
                if (ExcludedDirs.Contains(dirName))
                    continue;
                if (dirName.StartsWith("."))
                    continue;
                result.Add(dirName);
            }

            return result;
        }

        private static bool IsMiniGameGroup(string groupName)
        {
            return Directory.Exists($"{MiniGamesRoot}/{groupName}")
                   || groupName.StartsWith("Game");
        }

        #endregion

        #region Helpers

        private static AssetBundleCollectorSetting LoadSetting()
        {
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(CollectorSettingPath);
            if (setting == null)
                Debug.LogError($"[MiniGameCollector] 未找到 AB 收集器配置: {CollectorSettingPath}");
            return setting;
        }

        private static AssetBundleCollectorPackage GetDefaultPackage(AssetBundleCollectorSetting setting)
        {
            var package = setting.Packages.FirstOrDefault(p => p.PackageName == BuildUtils.PackageName);
            if (package == null)
                Debug.LogError($"[MiniGameCollector] 未找到 {BuildUtils.PackageName}");
            return package;
        }

        private static AssetBundleCollectorGroup CreateGroup(string name, string desc, string tag)
        {
            return new AssetBundleCollectorGroup
            {
                GroupName = name,
                GroupDesc = desc,
                AssetTags = tag,
                ActiveRuleName = nameof(EnableGroup),
                Collectors = new List<AssetBundleCollector>()
            };
        }

        private static AssetBundleCollector CreateCollector(
            string path, string tag, string packRule, string filterRule)
        {
            return new AssetBundleCollector
            {
                CollectPath = path,
                CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = packRule,
                FilterRuleName = filterRule,
                AssetTags = tag,
                UserData = string.Empty
            };
        }

        #endregion

        #region Cleanup Legacy

        /// <summary>
        /// 移除旧的 Default Group（从手动配置迁移到自动化后调用一次）。
        /// </summary>
        [MenuItem("JulyGF/资源管理/清理旧 Default Group", priority = 61)]
        public static void RemoveDefaultGroup()
        {
            var setting = LoadSetting();
            if (setting == null) return;

            var package = GetDefaultPackage(setting);
            if (package == null) return;

            var defaultGroup = package.Groups.FirstOrDefault(g => g.GroupName == "Default Group");
            if (defaultGroup == null)
            {
                Debug.Log("[MiniGameCollector] 未找到 Default Group，无需清理");
                return;
            }

            package.Groups.Remove(defaultGroup);
            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            Debug.Log("[MiniGameCollector] 已移除旧 Default Group");
        }

        #endregion
    }
}
