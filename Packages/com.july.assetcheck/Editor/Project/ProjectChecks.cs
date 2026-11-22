using System;
using System.Collections.Generic;
using System.Linq;

namespace July.AssetCheck
{
    internal sealed class ProjectColorSpaceRule : IAssetCheckRule<ProjectCheckContext>
    {
        public void Check(ProjectCheckContext context)
        {
            var path = context.AssetPath;
            if (path != AssetCheckTarget.PlayerSettingsPath) return;
            var standards = context.Standards;
            if (context.ColorSpace != standards.colorSpace)
                context.Add(new AssetCheckIssue(path, path, AssetCheckKind.Project, "project.color-space",
                    "Color Space", context.ColorSpace.ToString(), standards.colorSpace.ToString(), false));
        }
    }

    internal sealed class ProjectGraphicsApiRule : IAssetCheckRule<ProjectCheckContext>
    {
        public void Check(ProjectCheckContext context)
        {
            var path = context.AssetPath;
            if (path != AssetCheckTarget.PlayerSettingsPath) return;
            var standards = context.Standards;
            var automatic = context.AutomaticGraphicsApi;
            var apis = context.GraphicsApis;
            if (automatic != standards.automaticGraphicsApi ||
                (!standards.automaticGraphicsApi && !apis.SequenceEqual(standards.graphicsApis)))
                context.Add(new AssetCheckIssue(path, path, AssetCheckKind.Project, "project.graphics-api",
                    "目标平台 Graphics API", automatic ? "自动选择" : string.Join(", ", apis),
                    standards.automaticGraphicsApi ? "自动选择" : string.Join(", ", standards.graphicsApis), false));
        }
    }

    internal sealed class ProjectQualityRule : IAssetCheckRule<ProjectCheckContext>
    {
        public void Check(ProjectCheckContext context)
        {
            var path = context.AssetPath;
            if (path != AssetCheckTarget.QualitySettingsPath) return;
            if (context.DefaultQualityUnverified != null)
            {
                context.Add(Issue(path, context.DefaultQualityUnverified, true));
                return;
            }
            var index = context.DefaultQualityLevel;
            if (index < 0 || index >= context.QualityLevelCount || !context.ActiveQualityLevels.Contains(index))
                context.Add(Issue(path, "默认质量档索引 " + index + " 无效或已排除目标平台", false));
        }

        private static AssetCheckIssue Issue(string path, string actual, bool unverified) => new AssetCheckIssue(path, path,
            AssetCheckKind.Project, "project.default-quality", "默认质量档", actual,
            "有效且适用于目标平台", false, unverified);
    }

    internal sealed class ProjectPipelineReferenceRule : IAssetCheckRule<ProjectCheckContext>
    {
        public void Check(ProjectCheckContext context)
        {
            var path = context.AssetPath;
            if (path == AssetCheckTarget.GraphicsSettingsPath)
            {
                SerializedReferenceChecks.Check(context.SettingsAsset, context,
                    include: property => property.propertyPath == "m_CustomRenderPipeline");
            }
            else if (path == AssetCheckTarget.QualitySettingsPath)
            {
                var properties = new HashSet<string>(context.ActiveQualityLevels.Select(level =>
                    "m_QualitySettings.Array.data[" + level + "].customRenderPipeline"), StringComparer.Ordinal);
                SerializedReferenceChecks.Check(context.SettingsAsset, context,
                    include: property => properties.Contains(property.propertyPath));
            }
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
