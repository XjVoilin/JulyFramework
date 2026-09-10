using System;
using System.Collections.Generic;
using System.Linq;
using July.Release;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// Jenkins / CI 无头构建入口。
    /// <para>
    /// 全量构建（CoreVersion 自动 = PlanVersion）:
    ///   Unity -batchmode -quit -executeMethod July.Release.Editor.BuildPipelineCI.FullBuild
    ///         -platform WeChat -planVersion 1.0.0 [-debug] [-uploadCdn] [-miniGame] [-forceRebuild] [-aotBackupOutputPath "D:/AOT/full/aot"]
    /// </para>
    /// <para>
    /// 热更构建（显式输入从清单识别 CoreVersion；没有输入路径和版本时才自动选择本地备份）:
    ///   Unity -batchmode -quit -executeMethod July.Release.Editor.BuildPipelineCI.HotUpdateBuild
    ///         -platform WeChat -planVersion 1.0.2 [-aotBackupVersion 1.0.0] [-debug] [-uploadCdn] [-forceRebuild] [-aotBackupInputPath "D:/AOT/full/aot"]
    /// </para>
    /// <para>
    /// 单步 / 多步执行:
    ///   Unity -batchmode -quit -executeMethod July.Release.Editor.BuildPipelineCI.RunStep
    ///         -step Upload -planVersion 1.0.3
    ///   ... -steps AB,Upload
    /// </para>
    /// <para>
    /// 注意：不接受 -coreVersion / -appVersion / -resVersion 参数。
    ///   CoreVersion 由构建入口方法自动推导（见 spec §4.4）。
    ///   PlanVersion 通过 -planVersion 必填。
    /// </para>
    /// <para>
    /// 使用 -uploadCdn 明确要求 CDN 上传；凭证由本机配置或 CI 注入，不提交到 Git。
    /// </para>
    /// </summary>
    public static class BuildPipelineCI
    {
        public static void FullBuild()
        {
            ReleaseBuildReport.Clear();
            var ctx = ParseContext(nameof(FullBuild));
            if (ctx == null) return;

            // FullBuild 无条件同步 CoreVersion = PlanVersion（见 spec §4.4.1）
            ctx.CoreVersion = ctx.PlanVersion;

            // 写入主包 bundleVersion，供客户端运行时 Application.version 读取
            if (PlayerSettings.bundleVersion != ctx.CoreVersion)
            {
                Debug.Log($"[CI] 同步 PlayerSettings.bundleVersion: {PlayerSettings.bundleVersion} → {ctx.CoreVersion}");
                PlayerSettings.bundleVersion = ctx.CoreVersion;
                AssetDatabase.SaveAssets();
            }

            SyncBuildConfigPlanVersion(ctx.PlanVersion);

            Debug.Log($"[CI] FullBuild: CoreVersion={ctx.CoreVersion}  PlanVersion={ctx.PlanVersion}  Platform={ctx.Platform}");

            var steps = PipelinePresets.FullBuild(
                upload: HasFlag("-uploadCdn"),
                miniGame: HasFlag("-miniGame"));
            RunAndExit(ctx, steps, nameof(FullBuild));
        }

        public static void HotUpdateBuild()
        {
            ReleaseBuildReport.Clear();
            var ctx = ParseContext(nameof(HotUpdateBuild));
            if (ctx == null) return;

            if (ctx.AotBackupInputPath != null)
            {
                try { AotBackupStore.SelectInput(ctx); }
                catch (Exception exception) when (exception is System.IO.InvalidDataException || exception is System.IO.IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
                {
                    Debug.LogError($"[CI] 指定 AOT 备份无效: {exception.Message}");
                    EditorApplication.Exit(1);
                    return;
                }
            }
            // 只有未指定持久输入路径时才沿用本地自动选择规则。
            else if (string.IsNullOrEmpty(ctx.AOTBackupVersion))
            {
                if (!AutoSelectBackupVersion(ctx)) return; // 失败时内部已 Exit
            }

            // HotUpdate：CoreVersion 沿用 AOT 备份版本；**不改写 PlayerSettings.bundleVersion**（主包不动）
            ctx.CoreVersion = ctx.AOTBackupVersion;

            SyncBuildConfigPlanVersion(ctx.PlanVersion);

            Debug.Log(
                $"[CI] HotUpdateBuild: CoreVersion={ctx.CoreVersion}  PlanVersion={ctx.PlanVersion}  " +
                $"Platform={ctx.Platform}  AOTBackup={ctx.AOTBackupVersion}");

            var steps = PipelinePresets.HotUpdate(upload: HasFlag("-uploadCdn"));
            RunAndExit(ctx, steps, nameof(HotUpdateBuild));
        }

        /// <summary>
        /// 单步 / 多步执行入口。通过 -step Name 或 -steps A,B,C 指定。
        /// CoreVersion 使用 -aotBackupVersion（若指定），否则沿用 PlayerSettings.bundleVersion。
        /// 此入口不改变主包版本；创建新主包请使用 FullBuild。
        /// </summary>
        public static void RunStep()
        {
            ReleaseBuildReport.Clear();
            var ctx = ParseContext(nameof(RunStep));
            if (ctx == null) return;
            var args = Environment.GetCommandLineArgs();
            string stepArg = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-step":
                    case "-steps":
                        stepArg = SafeNextArg(args, ref i, args[i]);
                        break;
                }
            }

            if (string.IsNullOrEmpty(stepArg))
            {
                Debug.LogError("[CI] RunStep 需要 -step <Name> 或 -steps <A,B,...> 参数");
                EditorApplication.Exit(1);
                return;
            }

            var names = stepArg.Split(',');
            var steps = new List<BuildStep>();

            foreach (var name in names)
            {
                var trimmed = name.Trim();
                var step = ResolveStep(trimmed);
                if (step == null)
                {
                    Debug.LogError($"[CI] 未知步骤: {trimmed}。可用步骤: {string.Join(", ", StepNames)}");
                    EditorApplication.Exit(1);
                    return;
                }

                steps.Add(step);
            }

            ctx.UseExistingCoreVersion(PlayerSettings.bundleVersion);
            Debug.Log($"[CI] 单步执行: {string.Join(" → ", names)} CoreVersion={ctx.CoreVersion} PlanVersion={ctx.PlanVersion}");
            RunAndExit(ctx, steps, nameof(RunStep));
        }

        static readonly string[] StepNames =
        {
            "HybridCLRInstall", "HybridCLR", "AB",
            "AOTBackup", "AOTArchive", "AOTRestore", "AotHash", "HotUpdate",
            "Upload", "MiniGame", "DataFileUpload", "PreloadInjection", "PreloadJsonUpdate",
        };

        static BuildStep ResolveStep(string name) => name switch
        {
            "HybridCLRInstall" => new HybridCLRInstallStep(),
            "HybridCLR"        => new HybridCLRGenerateAllStep(),
            "AB"               => new AssetBundleBuildStep(),
            "AOTBackup"        => new AOTBackupStep(),
            "AOTArchive"       => new AOTBackupArchiveStep(),
            "AOTRestore"       => new AOTBackupRestoreStep(),
            "AotHash"          => new AotSourceHashStep(),
            "HotUpdate"        => new HybridCLRHotUpdateStep(),
            "Upload"           => new CloudUploadStep(),
            "MiniGame"         => new MiniGameBuildStep(),
            "DataFileUpload"   => new DataFileUploadStep(),
            "PreloadInjection" => new PreloadInjectionStep(),
            "PreloadJsonUpdate"=> new PreloadJsonUpdateStep(),
            _                  => null,
        };

        /// <summary>
        /// 选择 HotUpdate 要沿用的 AOT 备份版本。
        /// 规则（见 spec §4.4.3）：在所有 ≤ ctx.PlanVersion 的候选中取最大。
        /// 若无任何候选，硬失败（不回退到全局最大）。
        /// </summary>
        /// <returns>true 选择成功（已写入 ctx.AOTBackupVersion）；false 硬失败并已 EditorApplication.Exit(1)。</returns>
        static bool AutoSelectBackupVersion(BuildContext ctx)
        {
            var candidates = HybridCLRBuildHelper.GetAvailableBackupVersions(ctx.Target, ctx.Platform);
            if (candidates.Length == 0)
            {
                Debug.LogError("[CI] 未找到任何 AOT 备份（workspace 和持久归档均无）。请先执行一次 FullBuild。");
                EditorApplication.Exit(1);
                return false;
            }

            if (!Version.TryParse(ctx.PlanVersion, out var planVer))
            {
                Debug.LogError($"[CI] PlanVersion 格式非法: {ctx.PlanVersion}");
                EditorApplication.Exit(1);
                return false;
            }

            string best = null;
            Version bestVer = null;
            foreach (var c in candidates)
            {
                if (!Version.TryParse(c, out var cVer)) continue; // 跳过无法解析的目录名
                if (cVer > planVer) continue;                     // 候选 > PlanVersion 的丢弃
                if (bestVer == null || cVer > bestVer)
                {
                    bestVer = cVer;
                    best = c;
                }
            }

            if (best == null)
            {
                Debug.LogError(
                    $"[CI] 在所有 AOT 备份中找不到不晚于 PlanVersion {ctx.PlanVersion} 的版本。\n" +
                    $"候选: [{string.Join(", ", candidates)}]\n" +
                    "可能原因：1) 当前分支从未做过 FullBuild；2) PlanVersion 被手动写小了。\n" +
                    "解决：先跑一次 FullBuild 或显式传 -aotBackupVersion <x.y.z>。");
                EditorApplication.Exit(1);
                return false;
            }

            ctx.AOTBackupVersion = best;
            Debug.Log($"[CI] AutoSelectBackupVersion: 候选=[{string.Join(", ", candidates)}]，取 ≤{ctx.PlanVersion} 最大 → {best}");
            return true;
        }

        static void RunAndExit(BuildContext ctx, IReadOnlyList<BuildStep> steps, string buildType)
        {
            ctx.Interactive = false;
            var result = new July.Build.BuildRunner(new July.Build.UnityBuildHost())
                .Run(ctx, steps);

            ReleaseBuildReport.Save(ctx, result, buildType, HasFlag("-uploadCdn"));

            if (result.Succeeded)
            {
                Debug.Log($"[CI] 构建成功 ({result.Elapsed.TotalSeconds:F1}s)");
            }
            else
            {
                Debug.LogError($"[CI] 构建失败: [{result.FailedStep}] {result.Error}");
                EditorApplication.Exit(1);
            }
        }

        static bool HasFlag(string flag)
        {
            return Array.Exists(Environment.GetCommandLineArgs(), a => a == flag);
        }

        /// <summary>
        /// 把 PlanVersion 回写 BuildConfig.planVersion（供 VersionPanel 只读展示）。
        /// workspace 本地修改，Jenkins 不 commit（按 spec §2.4）。
        /// </summary>
        static void SyncBuildConfigPlanVersion(string planVersion)
        {
            var buildConfig = ReleaseProject.LoadBuildConfig();
            if (buildConfig == null || buildConfig.planVersion == planVersion) return;
            buildConfig.planVersion = planVersion;
            EditorUtility.SetDirty(buildConfig.Asset);
            AssetDatabase.SaveAssets();
        }

        static BuildContext ParseContext(string entryPoint)
        {
            var args = Environment.GetCommandLineArgs();
            var ctx = new BuildContext
            {
                Target = EditorUserBuildSettings.activeBuildTarget,
            };

            if (!TryApplyAotArguments(args, entryPoint, ctx)) return null;

            for (var i = 0; i < args.Length; i++)
            {
                if (string.IsNullOrEmpty(args[i]) || args[i][0] != '-') continue;

                switch (args[i])
                {
                    case "-platform":
                        var rawPlatform = SafeNextArg(args, ref i, "-platform");
                        ctx.Platform = System.Array.Find(PlatformKeys.Options,
                            k => string.Equals(k, rawPlatform, System.StringComparison.OrdinalIgnoreCase)) ?? rawPlatform;
                        break;
                    case "-planVersion":
                        ctx.PlanVersion = SafeNextArg(args, ref i, "-planVersion");
                        break;
                    case "-aotBackupVersion":
                        ctx.AOTBackupVersion = SafeNextArg(args, ref i, "-aotBackupVersion");
                        break;
                    case "-debug":
                        ctx.Development = true;
                        break;
                    case "-development":
                        Debug.LogWarning("[CI] -development is deprecated, use -debug instead");
                        ctx.Development = true;
                        break;
                    case "-forceRebuild":
                        ctx.ForceRebuild = true;
                        break;
                    case "-strictMetadataCheck":
                        ctx.StrictMetadataCheck = true;
                        break;
                    case "-env":
                        ctx.Env = SafeNextArg(args, ref i, "-env");
                        break;
                    // 显式拒绝历史/禁用参数，早失败便于排查 Jenkins 误配
                    case "-appVersion":
                    case "-resVersion":
                    case "-coreVersion":
                        Debug.LogError($"[CI] 参数 {args[i]} 已废弃。请改用 -planVersion；CoreVersion 由构建入口自动推导（见 spec §4.4）。");
                        EditorApplication.Exit(1);
                        return null;
                }
            }

            if (string.IsNullOrEmpty(ctx.PlanVersion))
            {
                Debug.LogError("[CI] 缺少必填参数 -planVersion <x.y.z>");
                EditorApplication.Exit(1);
                return null;
            }

            if (!BuildUtils.IsValidVersion(ctx.PlanVersion))
            {
                Debug.LogError($"[CI] -planVersion 格式非法: {ctx.PlanVersion}（需要 x.y.z）");
                EditorApplication.Exit(1);
                return null;
            }

            if (!string.IsNullOrEmpty(ctx.Platform) && !PlatformKeys.Options.Contains(ctx.Platform))
                throw new ArgumentException($"Unsupported platform: {ctx.Platform}");
            if (!string.IsNullOrEmpty(ctx.Env))
            {
                if (!Enum.TryParse<ReleaseEnvironment>(ctx.Env, true, out var environment) ||
                    !Enum.IsDefined(typeof(ReleaseEnvironment), environment))
                    throw new ArgumentException($"Unsupported environment: {ctx.Env}");
                ctx.Env = environment.ToString();
            }
            if (!string.IsNullOrEmpty(ctx.Platform))
                EditorPlatformPref.Platform = ctx.Platform;
            else
                ctx.Platform = EditorPlatformPref.Platform;

            var buildConfig = ReleaseProject.LoadBuildConfig();
            if (buildConfig != null)
                ctx.CloudUrl = buildConfig.cloudUrl;

            var bootConfig = ReleaseProject.LoadBootConfig();
            if (bootConfig != null)
            {
                ctx.CdnUrl = bootConfig.cdnUrl;

                if (!string.IsNullOrEmpty(ctx.Env) && Enum.TryParse<ReleaseEnvironment>(ctx.Env, true, out var envVal) && bootConfig.env != envVal)
                {
                    Debug.Log($"[CI] 同步 BootConfig.env: {bootConfig.env} → {envVal}");
                    bootConfig.env = envVal;
                    EditorUtility.SetDirty(bootConfig.Asset);
                    AssetDatabase.SaveAssets();
                }

                if (string.IsNullOrEmpty(ctx.Env))
                    ctx.Env = bootConfig.EnvName;
            }

            return ctx;
        }

        /// <summary>
        /// CI 预构建入口：同步平台编译宏。
        /// 改 define 会触发脚本重编译 + domain reload，当前 -executeMethod 不会重新执行，
        /// 因此必须作为独立的 Unity 调用，在 FullBuild/HotUpdateBuild 之前运行。
        /// <para>
        /// 用法：Unity -batchmode -quit -executeMethod July.Release.Editor.BuildPipelineCI.SyncPlatformDefines
        ///       -platform TikTok [-debug]
        /// </para>
        /// </summary>
        public static void SyncPlatformDefines()
        {
            if (!TryApplyAotArguments(Environment.GetCommandLineArgs(), nameof(SyncPlatformDefines), new BuildContext())) return;
            var args = Environment.GetCommandLineArgs();
            string platform = null;
            bool debug = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-platform":
                        platform = SafeNextArg(args, ref i, "-platform");
                        break;
                    case "-debug":
                        debug = true;
                        break;
                }
            }

            platform = Array.Find(PlatformKeys.Options,
                k => string.Equals(k, platform, StringComparison.OrdinalIgnoreCase)) ?? platform;

            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError("[CI] SyncPlatformDefines: -platform <WeChat|TikTok> is required");
                EditorApplication.Exit(1);
                return;
            }

            PlatformPreparation.Apply(platform, debug);
        }

        static bool TryApplyAotArguments(string[] args, string entryPoint, BuildContext context)
        {
            try { AotBackupArguments.Apply(args, entryPoint, context); return true; }
            catch (Exception exception) when (exception is ArgumentException || exception is System.IO.IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                Debug.LogError($"[CI] AOT 路径参数错误: {exception.Message}");
                EditorApplication.Exit(1);
                return false;
            }
        }

        static string SafeNextArg(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
            {
                Debug.LogError($"[CI] 参数 {flag} 缺少值");
                return null;
            }

            return args[++i];
        }
    }
}
