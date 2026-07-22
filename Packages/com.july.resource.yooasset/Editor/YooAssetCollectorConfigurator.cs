using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace July.Resource.YooAsset
{
    public sealed class YooAssetCollectorGroupDefinition
    {
        public string Name { get; }
        public string Description { get; }
        public string Tag { get; }
        public string CollectDirectory { get; }

        public YooAssetCollectorGroupDefinition(string name, string description,
            string tag, string collectDirectory)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            Tag = tag ?? string.Empty;
            CollectDirectory = collectDirectory ??
                               throw new ArgumentNullException(nameof(collectDirectory));
        }
    }

    /// <summary>Idempotent editor configuration for YooAsset collector groups.</summary>
    public static class YooAssetCollectorConfigurator
    {
        public static bool HasGroups(string settingPath, string packageName,
            params string[] groupNames)
        {
            var package = FindPackage(settingPath, packageName);
            return package != null && groupNames.All(name =>
                package.Groups.Any(group => group.GroupName == name));
        }

        public static bool HasGroups(string settingPath, string packageName,
            IReadOnlyList<YooAssetCollectorGroupDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var package = FindPackage(settingPath, packageName);
            return package != null && definitions.All(definition =>
                package.Groups.Any(group => Matches(group, definition)));
        }

        public static bool EnsureGroups(string settingPath, string packageName,
            IReadOnlyList<YooAssetCollectorGroupDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(settingPath);
            if (setting == null)
            {
                Debug.LogError($"[YooAsset] Collector setting was not found: {settingPath}");
                return false;
            }

            var package = setting.Packages.FirstOrDefault(item => item.PackageName == packageName);
            if (package == null)
            {
                Debug.LogError($"[YooAsset] Collector package was not found: {packageName}");
                return false;
            }

            var directoryCreated = false;
            foreach (var definition in definitions)
            {
                if (!Directory.Exists(definition.CollectDirectory))
                {
                    Directory.CreateDirectory(definition.CollectDirectory);
                    directoryCreated = true;
                }
            }

            if (directoryCreated)
                AssetDatabase.Refresh();

            var settingChanged = false;
            foreach (var definition in definitions)
            {
                var group = package.Groups.FirstOrDefault(item =>
                    item.GroupName == definition.Name);
                if (group == null)
                {
                    package.Groups.Add(CreateGroup(definition));
                    settingChanged = true;
                }
                else
                {
                    settingChanged |= UpdateGroup(group, definition);
                }
            }

            if (settingChanged)
            {
                EditorUtility.SetDirty(setting);
                AssetDatabase.SaveAssets();
            }
            return true;
        }

        private static AssetBundleCollectorPackage FindPackage(string settingPath,
            string packageName)
        {
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(settingPath);
            return setting?.Packages.FirstOrDefault(item => item.PackageName == packageName);
        }

        private static AssetBundleCollectorGroup CreateGroup(
            YooAssetCollectorGroupDefinition definition) => new()
        {
            GroupName = definition.Name,
            GroupDesc = definition.Description,
            AssetTags = definition.Tag,
            ActiveRuleName = nameof(EnableGroup),
            Collectors =
            {
                CreateCollector(definition)
            }
        };

        private static bool UpdateGroup(AssetBundleCollectorGroup group,
            YooAssetCollectorGroupDefinition definition)
        {
            var changed = false;
            changed |= Set(ref group.GroupDesc, definition.Description);
            changed |= Set(ref group.AssetTags, definition.Tag);
            changed |= Set(ref group.ActiveRuleName, nameof(EnableGroup));

            var collectPath = NormalizePath(definition.CollectDirectory);
            var collector = group.Collectors.FirstOrDefault(item =>
                NormalizePath(item.CollectPath) == collectPath);
            if (collector == null && group.Collectors.Count == 1)
                collector = group.Collectors[0];
            if (collector == null)
            {
                collector = CreateCollector(definition);
                group.Collectors.Add(collector);
                return true;
            }

            changed |= Set(ref collector.CollectPath, collectPath);
            changed |= Set(ref collector.CollectorGUID,
                AssetDatabase.AssetPathToGUID(collectPath));
            changed |= Set(ref collector.CollectorType, ECollectorType.MainAssetCollector);
            changed |= Set(ref collector.AddressRuleName, nameof(AddressByFileName));
            changed |= Set(ref collector.PackRuleName, nameof(PackDirectory));
            changed |= Set(ref collector.FilterRuleName, nameof(CollectAll));
            changed |= Set(ref collector.AssetTags, definition.Tag);
            return changed;
        }

        private static bool Matches(AssetBundleCollectorGroup group,
            YooAssetCollectorGroupDefinition definition)
        {
            if (group.GroupName != definition.Name ||
                group.GroupDesc != definition.Description ||
                group.AssetTags != definition.Tag ||
                group.ActiveRuleName != nameof(EnableGroup))
                return false;

            var expectedPath = NormalizePath(definition.CollectDirectory);
            var expectedGuid = AssetDatabase.AssetPathToGUID(expectedPath);
            return group.Collectors.Any(collector =>
                NormalizePath(collector.CollectPath) == expectedPath &&
                collector.CollectorGUID == expectedGuid &&
                collector.CollectorType == ECollectorType.MainAssetCollector &&
                collector.AddressRuleName == nameof(AddressByFileName) &&
                collector.PackRuleName == nameof(PackDirectory) &&
                collector.FilterRuleName == nameof(CollectAll) &&
                collector.AssetTags == definition.Tag);
        }

        private static AssetBundleCollector CreateCollector(
            YooAssetCollectorGroupDefinition definition)
        {
            var collectPath = NormalizePath(definition.CollectDirectory);
            return new AssetBundleCollector
            {
                CollectPath = collectPath,
                CollectorGUID = AssetDatabase.AssetPathToGUID(collectPath),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
                AssetTags = definition.Tag,
            };
        }

        private static string NormalizePath(string path) =>
            path.Replace('\\', '/').TrimEnd('/');

        private static bool Set<T>(ref T target, T value)
        {
            if (EqualityComparer<T>.Default.Equals(target, value)) return false;
            target = value;
            return true;
        }
    }
}
