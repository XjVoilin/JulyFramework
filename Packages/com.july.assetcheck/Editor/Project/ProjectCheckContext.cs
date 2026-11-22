using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    public sealed class ProjectCheckContext : AssetCheckContext
    {
        public ProjectStandards Standards { get; }
        internal ColorSpace ColorSpace { get; }
        internal bool AutomaticGraphicsApi { get; }
        internal IReadOnlyList<GraphicsDeviceType> GraphicsApis { get; }
        internal Object SettingsAsset { get; }
        internal IReadOnlyList<int> ActiveQualityLevels { get; }
        internal int QualityLevelCount { get; }
        internal int DefaultQualityLevel { get; }
        internal string DefaultQualityUnverified { get; }

        internal ProjectCheckContext(string path, ProjectStandards standards, List<AssetCheckIssue> issues)
            : base(path, AssetCheckKind.Project, issues)
        {
            Standards = standards;
            switch (path)
            {
                case AssetCheckTarget.PlayerSettingsPath:
                    ColorSpace = PlayerSettings.colorSpace;
                    AutomaticGraphicsApi = PlayerSettings.GetUseDefaultGraphicsAPIs(ProjectRenderConfiguration.Target);
                    GraphicsApis = PlayerSettings.GetGraphicsAPIs(ProjectRenderConfiguration.Target);
                    break;
                case AssetCheckTarget.QualitySettingsPath:
                    SettingsAsset = ProjectRenderConfiguration.LoadSettings(path);
                    ActiveQualityLevels = ProjectRenderConfiguration.QualityLevels;
                    QualityLevelCount = QualitySettings.names.Length;
                    (DefaultQualityLevel, DefaultQualityUnverified) = ReadDefaultQuality(SettingsAsset);
                    break;
                case AssetCheckTarget.GraphicsSettingsPath:
                    SettingsAsset = ProjectRenderConfiguration.LoadSettings(path);
                    break;
            }
        }

        private static (int index, string unverified) ReadDefaultQuality(Object settings)
        {
            using var serialized = new SerializedObject(settings);
            var defaults = serialized.FindProperty("m_PerPlatformDefaultQuality");
            if (defaults == null) return (0, "无法读取默认质量档结构");
            foreach (SerializedProperty entry in defaults)
            {
                if (entry.FindPropertyRelative("first").stringValue == ProjectRenderConfiguration.PlatformName)
                    return (entry.FindPropertyRelative("second").intValue, null);
            }
            return (0, "未找到目标平台默认质量档条目");
        }
    }
}
