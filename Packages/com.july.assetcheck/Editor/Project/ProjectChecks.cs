using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class ProjectColorSpaceRule : IAssetCheckRule<ProjectCheckContext>
    {
        public string File => "ProjectSettings/ProjectSettings.asset";
        public void Check(ProjectCheckContext context)
        {
            if (context.AssetPath != File) return;
            var standards = context.Standards;
            if (PlayerSettings.colorSpace != standards.colorSpace)
                context.Add(new AssetCheckIssue(File, File, AssetCheckKind.Project, "project.color-space",
                    "Color Space", PlayerSettings.colorSpace.ToString(), standards.colorSpace.ToString(), false));
        }
    }

    internal sealed class ProjectGraphicsApiRule : IAssetCheckRule<ProjectCheckContext>
    {
        public string File => "ProjectSettings/ProjectSettings.asset";
        public void Check(ProjectCheckContext context)
        {
            if (context.AssetPath != File) return;
            var standards = context.Standards;
            var target = ProjectRenderConfiguration.Target;
            var automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            var apis = PlayerSettings.GetGraphicsAPIs(target);
            if (automatic != standards.automaticGraphicsApi ||
                (!standards.automaticGraphicsApi && !apis.SequenceEqual(standards.graphicsApis)))
                context.Add(new AssetCheckIssue(File, File, AssetCheckKind.Project, "project.graphics-api",
                    "目标平台 Graphics API", automatic ? "自动选择" : string.Join(", ", apis),
                    standards.automaticGraphicsApi ? "自动选择" : string.Join(", ", standards.graphicsApis), false));
        }
    }

    internal sealed class ProjectQualityRule : IAssetCheckRule<ProjectCheckContext>
    {
        public string File => "ProjectSettings/QualitySettings.asset";
        public void Check(ProjectCheckContext context)
        {
            if (context.AssetPath != File) return;
            var standards = context.Standards;
            using (var serialized = new SerializedObject(ProjectRenderConfiguration.LoadSettings(File)))
            {
                var defaults = serialized.FindProperty("m_PerPlatformDefaultQuality");
                if (defaults == null)
                {
                    context.Add(Issue("无法读取默认质量档结构", true));
                    return;
                }
                foreach (SerializedProperty entry in defaults)
                {
                    if (entry.FindPropertyRelative("first").stringValue != ProjectRenderConfiguration.PlatformName)
                        continue;
                    var index = entry.FindPropertyRelative("second").intValue;
                    if (index < 0 || index >= QualitySettings.names.Length ||
                        !ProjectRenderConfiguration.QualityLevels.Contains(index))
                        context.Add(Issue("默认质量档索引 " + index + " 无效或已排除目标平台", false));
                    return;
                }
                context.Add(Issue("未找到目标平台默认质量档条目", true));
            }
        }

        private AssetCheckIssue Issue(string actual, bool unverified) => new AssetCheckIssue(File, File,
            AssetCheckKind.Project, "project.default-quality", "默认质量档", actual,
            "有效且适用于目标平台", false, unverified);
    }

    internal sealed class ProjectPipelineReferenceRule : IAssetCheckRule<ProjectCheckContext>
    {
        public void Check(ProjectCheckContext context)
        {
            var path = context.AssetPath;
            if (path != "ProjectSettings/GraphicsSettings.asset" && path != "ProjectSettings/QualitySettings.asset") return;
            var target = ProjectRenderConfiguration.LoadSettings(path);
            var levels = new HashSet<int>(ProjectRenderConfiguration.QualityLevels);
            SerializedReferenceChecks.Check(target, context, include: property =>
            {
                if (path.EndsWith("GraphicsSettings.asset", StringComparison.Ordinal))
                    return property.propertyPath == "m_CustomRenderPipeline";
                foreach (var level in levels)
                    if (property.propertyPath == "m_QualitySettings.Array.data[" + level + "].customRenderPipeline")
                        return true;
                return false;
            });
        }
    }

    internal sealed class ProjectCheckModule : IAssetCheckModule
    {
        private readonly IAssetCheckRule<ProjectCheckContext>[] _rules = AssetCheckRules<ProjectCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.Project;
        public string RulesFingerprint => AssetCheckRules<ProjectCheckContext>.Fingerprint;
        public string[] Collect(string scope) => AssetCheckTarget.ProjectFiles;
        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var issues = new List<AssetCheckIssue>();
            AssetCheckRules<ProjectCheckContext>.Check(_rules, new ProjectCheckContext(assetPath, standards.project, issues));
            return issues;
        }
        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards) =>
            throw new InvalidOperationException("项目设置由 Unity 设置面板或现有发布工具修改。");
    }
}
