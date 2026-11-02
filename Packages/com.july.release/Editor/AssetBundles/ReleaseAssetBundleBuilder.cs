using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Tasks;
using YooAsset;
using YooAsset.Editor;
using YooTask = YooAsset.Editor.IBuildTask;
using SbpResult = UnityEditor.Build.Pipeline.Interfaces.IBundleBuildResults;

namespace July.Release.Editor
{
    public sealed class AssetBundleAnalysisBuild
    {
        public string ReportPath { get; }
        public string PackageDirectory { get; }
        internal AssetBundleAnalysisBuild(string report, string directory) { ReportPath = report; PackageDirectory = directory; }
    }

    public static class ReleaseAssetBundleBuilder
    {
        public static bool IsBuilding { get; private set; }

        // 分析只使用现有构建输入，不触发 AOT、热更编译或正式发布编排。
        public static AssetBundleAnalysisBuild BuildAnalysis(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("请先切换到 " + target + " 并等待编译完成；资源工具的分析构建入口可自动完成切换。");
            if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("脚本编译失败，不能构建分析 AB。");
            var profile = ReleaseProject.Profile;
            RequireDllInput(profile.HybridCLR.HotUpdateDllDirectory);
            RequireDllInput(profile.HybridCLR.AotMetadataDirectory);
            var parameters = CreateParameters(AnalysisRoot(), target, profile.PackageName, DateTime.Now.ToString("yyyyMMddHHmmssfff"), EBuildinFileCopyOption.None, "");
            ValidateAnalysisOutput(parameters);
            var collector = new BundleSizeCollector();
            var result = Build(parameters, profile.MergeSharedBundles, collector);
            if (!result.Success) throw new InvalidOperationException("分析 AB 构建失败：" + result.ErrorInfo);
            string report = Directory.GetFiles(result.OutputPackageDirectory, "*.report").Single();
            AssetBundleSizeSnapshot.Write(report, collector.Bundles);
            return new AssetBundleAnalysisBuild(report, result.OutputPackageDirectory);
        }
        internal static YooAsset.Editor.BuildResult BuildRelease(BuildContext context, EBuildinFileCopyOption copy, string copyParameters)
        {
            var parameters = CreateParameters(AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(), context.Target, BuildUtils.PackageName, context.PackageVersion, copy, copyParameters);
            return Build(parameters, ReleaseProject.Profile.MergeSharedBundles, null);
        }
        internal static string AnalysisRoot() => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(AssetBundleBuilderHelper.GetDefaultBuildOutputRoot())), "BundlesAnalysis");
        internal static void ValidateAnalysisOutput(ScriptableBuildParameters parameters)
        {
            var root = Path.GetFullPath(AnalysisRoot()) + Path.DirectorySeparatorChar;
            var comparison = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var output in new[] { parameters.GetPipelineOutputDirectory(), parameters.GetPackageOutputDirectory() })
                if (!Path.GetFullPath(output).StartsWith(root, comparison)) throw new InvalidOperationException("分析输出越过 BundlesAnalysis 目录，请检查资源包名称。");
        }
        internal static ScriptableBuildParameters CreateParameters(string root, BuildTarget target, string package, string version, EBuildinFileCopyOption copy, string copyParameters) => new ScriptableBuildParameters
        {
            BuildOutputRoot = root,
            BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = BuildUtils.YooAssetPipelineType,
            BuildBundleType = (int)EBuildBundleType.AssetBundle,
            BuildTarget = target,
            PackageName = package,
            PackageVersion = version,
            EnableSharePackRule = true,
            SingleReferencedPackAlone = false,
            VerifyBuildingResult = true,
            FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(package, BuildUtils.YooAssetPipelineType),
            BuildinFileCopyOption = copy,
            BuildinFileCopyParams = copyParameters,
            CompressOption = ECompressOption.LZ4,
            ClearBuildCacheFiles = false,
            UseAssetDependencyDB = true,
            BuiltinShadersBundleName = "shared_unity_shaders",
        };
        internal static YooTask CreateBuildMapTask(bool merge) => merge ? (YooTask)new CustomTaskGetBuildMap_SBP() : new TaskGetBuildMap_SBP();
        internal static List<YooTask> CreateTasks(bool merge, BundleSizeCollector collector)
        {
            var tasks = new List<YooTask>
            {
                new TaskPrepare_SBP(), CreateBuildMapTask(merge),
                collector == null ? (YooTask)new TaskBuilding_SBP() : new AnalysisBuildingTask(collector),
                new TaskVerifyBuildResult_SBP(), new TaskEncryption_SBP(), new TaskUpdateBundleInfo_SBP(),
                new TaskCreateManifest_SBP(), new TaskCreateReport_SBP(), new TaskCreatePackage_SBP(),
            };
            if (collector == null) { tasks.Add(new TaskCopyBuildinFiles_SBP()); tasks.Add(new TaskCreateCatalog_SBP()); }
            return tasks;
        }
        internal static YooAsset.Editor.BuildResult Build(ScriptableBuildParameters parameters, bool merge, BundleSizeCollector collector)
        {
            EnsureAvailable();
            IsBuilding = true;
            try
            {
                using (TMPFontSwapper.UseLaunchFont())
                    return new AssetBundleBuilder().Run(parameters, CreateTasks(merge, collector), true);
            }
            finally { IsBuilding = false; }
        }
        internal static void EnsureAvailable()
        {
            if (IsBuilding || UnityEditor.BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("已有构建正在进行，请等待结束。");
        }
        static void RequireDllInput(string path)
        {
            if (!Directory.Exists(path) || Directory.GetFiles(path, "*.dll.bytes").Length == 0)
                throw new InvalidOperationException("缺少现有 DLL 构建输入：" + path + "。请先完成项目构建准备；AB 分析不会自动执行 AOT 或热更编译。");
        }

        // Yoo 2.3 的构建任务未暴露 SBP 回调参数，仅在此适配其结果上下文；任务工厂继续复用原实现。
        sealed class AnalysisBuildingTask : YooTask
        {
            readonly BundleSizeCollector _collector;
            internal AnalysisBuildingTask(BundleSizeCollector collector) => _collector = collector;
            public void Run(YooAsset.Editor.BuildContext context)
            {
                var parameters = (ScriptableBuildParameters)context.GetContextObject<BuildParametersContext>().Parameters;
                var map = context.GetContextObject<BuildMapContext>();
                var tasks = SBPBuildTasks.Create(parameters.BuiltinShadersBundleName, parameters.MonoScriptsBundleName).ToList();
                tasks.Insert(tasks.FindIndex(task => task is ArchiveAndCompressBundles), _collector);
                var code = ContentPipeline.BuildAssetBundles(parameters.GetBundleBuildParameters(), new BundleBuildContent(map.GetPipelineBuilds()), out SbpResult result, tasks);
                if (code < 0) throw new InvalidOperationException("Unity AB 构建失败：" + code);
                foreach (var special in new[] { parameters.BuiltinShadersBundleName, parameters.MonoScriptsBundleName })
                    if (!string.IsNullOrEmpty(special) && result.BundleInfos.ContainsKey(special)) map.CreateEmptyBundleInfo(special);
                context.SetContextObject(new TaskBuilding_SBP.BuildResultContext { Results = result, BuiltinShadersBundleName = parameters.BuiltinShadersBundleName, MonoScriptsBundleName = parameters.MonoScriptsBundleName });
            }
        }
    }
}
