using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace July.Release.Editor
{
    /// <summary>只读的接入检查；窗口和流水线共用，不编译、不下载、不修改收集器。</summary>
    public static class ReleaseConfigurationCheck
    {
        public static IReadOnlyList<string> Inspect(BuildConfig config, BuildContext context,
            bool bundles, bool hybridClr, bool player, bool upload)
        {
            var errors = new List<string>();
            if (config.bootConfig is not IReleaseBootConfig)
                errors.Add("运行配置：请指定实现 IReleaseBootConfig 的资产。");
            var versionError = context.Validate();
            if (versionError != null) errors.Add(versionError);
            if (player || upload)
            {
                var urlError = BuildUtils.ValidateResourceUrls(context, out _, out _, out _);
                if (urlError != null) errors.Add(urlError);
            }
            if (upload)
            {
                try
                {
                    var executable = ReleaseConventions.CoscliExecutable;
                    if (!File.Exists(executable)) errors.Add($"Release 包内缺少 COSCLI：{executable}。请检查包内容。");
                }
                catch (PlatformNotSupportedException exception) { errors.Add(exception.Message); }
                if (!File.Exists(ReleaseConventions.CoscliConfig)) errors.Add($"COS 配置文件不存在：{ReleaseConventions.CoscliConfig}（框架约定路径）。");
            }
            if (player && !ReleaseProject.HasPlatformBuilder(context.Platform))
                errors.Add($"平台 {context.Platform} 尚未启用：安装 SDK，并在构建面板应用目标平台与编译宏。");
            if (bundles || hybridClr)
            {
                if (config.collectorSettings == null)
                    errors.Add("请在 BuildConfig / 构建接入中引用项目现有的 YooAsset 收集配置。");
                else if (config.bootConfig is IReleaseBootConfig)
                {
                    // SDK 构建读取其全局配置；引用必须与 SDK 使用的资产一致。
                    var collectors = AssetDatabase.FindAssets("t:AssetBundleCollectorSetting");
                    if (collectors.Length != 1 || AssetDatabase.GUIDToAssetPath(collectors[0]) != AssetDatabase.GetAssetPath(config.collectorSettings))
                        errors.Add("YooAsset 需要唯一的收集配置资产，并且与 BuildConfig 引用一致。");
                    foreach (var group in new[] { ReleaseConventions.HotFixGroup, ReleaseConventions.AotMetaGroup })
                    {
                        try { config.GetCollectorDirectory(group); }
                        catch (InvalidOperationException e) { errors.Add(e.Message); }
                        catch (ArgumentException e) { errors.Add(e.Message); }
                    }
                    if (errors.Count == 0 && BuildConfig.PathsOverlap(
                            config.GetCollectorDirectory(ReleaseConventions.HotFixGroup), config.GetCollectorDirectory(ReleaseConventions.AotMetaGroup)))
                        errors.Add("热更 DLL 与 AOT 元数据收集目录不能重叠。");
                }
                if (string.IsNullOrWhiteSpace(config.aot.sourceDirectory) || !Directory.Exists(config.aot.sourceDirectory))
                    errors.Add("AOT 源码检查目录不存在，请在 BuildConfig / AOT 中选择实际源码目录。");
            }
            if (bundles && config.sharedBundles.enabled &&
                (config.sharedBundles.mergeDepth < 1 || config.sharedBundles.fontMergeDepth < 1))
                errors.Add("共享包合并深度必须大于零。");
            if (player && (config.maxPreloadCount < 1 || config.maxPreloadBytes < 1))
                errors.Add("预下载数量和大小上限必须大于零。");
            return errors.Distinct().ToArray();
        }
    }

    internal sealed class ReleaseConfigurationStep : BuildStep
    {
        readonly bool _bundles, _hybridClr, _player, _upload;
        public ReleaseConfigurationStep(bool bundles, bool hybridClr, bool player, bool upload)
        { _bundles = bundles; _hybridClr = hybridClr; _player = player; _upload = upload; }
        public override string Name => "发布接入检查";
        public override string Validate(BuildContext context)
        {
            var errors = ReleaseConfigurationCheck.Inspect(ReleaseProject.LoadBuildConfig(), context,
                _bundles, _hybridClr, _player, _upload);
            return errors.Count == 0 ? null : string.Join("\n", errors);
        }
        public override bool Execute(BuildContext context) => true;
    }
}
