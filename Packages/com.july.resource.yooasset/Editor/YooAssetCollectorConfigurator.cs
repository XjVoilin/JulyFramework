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
                if (package.Groups.Any(group => group.GroupName == definition.Name))
                    continue;

                package.Groups.Add(CreateGroup(definition));
                settingChanged = true;
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
                new AssetBundleCollector
                {
                    CollectPath = definition.CollectDirectory,
                    CollectorGUID = AssetDatabase.AssetPathToGUID(definition.CollectDirectory),
                    CollectorType = ECollectorType.MainAssetCollector,
                    AddressRuleName = nameof(AddressByFileName),
                    PackRuleName = nameof(PackDirectory),
                    FilterRuleName = nameof(CollectAll),
                    AssetTags = definition.Tag,
                }
            }
        };
    }
}
