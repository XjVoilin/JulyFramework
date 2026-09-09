using System.IO;
using July.Release;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace July.Release.Editor
{
    /// <summary>
    /// 将本地 CDN 目录上传至腾讯云 COS。
    /// 凭证从 Tools/coscli/.cos.yaml 读取，依赖 ctx.CloudUrl 和本地 coscli 可执行文件。
    /// </summary>
    public sealed class CloudUploadStep : BuildStep
    {
        public override string Name => "上传到 COS";

        string _bucket, _pathPrefix, _coscliPath;

        public override string Validate(BuildContext ctx)
        {
            var err = BuildUtils.ValidateCosEnvironment(ctx, out _bucket, out _, out _pathPrefix, out _coscliPath);
            if (err != null) return err;

            var (liveVersion, versionMsg) = CheckLiveVersion(ctx);

            // 交互模式：版本冲突延迟到 Execute 弹窗确认，Validate 不拦截
            if (versionMsg != null && !ctx.Interactive)
                return versionMsg;

            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            // 交互模式下在 Execute 做版本冲突确认
            if (ctx.Interactive)
            {
                var (liveVersion, versionMsg) = CheckLiveVersion(ctx);
                if (versionMsg != null)
                {
                    if (!EditorUtility.DisplayDialog(
                            "版本覆盖确认",
                            $"Plan Version {ctx.PlanVersion} <= 线上版本 {liveVersion}\n\n" +
                            "继续上传将覆盖线上同版本资源。\n" +
                            "若需要回退版本，请在 ConfigServer 切换。",
                            "继续上传", "取消"))
                    {
                        Debug.LogWarning("[COS] 用户取消上传（版本覆盖确认）");
                        return false;
                    }

                    Debug.Log($"[COS] 用户确认覆盖线上版本 {liveVersion}，继续上传");
                }
            }

            var cdnDir = ctx.CdnOutputDir
                         ?? BuildUtils.GetCdnPath(ctx.Env, ctx.Platform, ctx.CoreVersion, ctx.PlanVersion);

            if (!Directory.Exists(cdnDir))
            {
                Debug.LogError($"[COS] 本地 CDN 目录不存在: {cdnDir}");
                return false;
            }

            var cosTarget = BuildUtils.BuildCosObjectUrl(_bucket, _pathPrefix,
                ctx.Env, ctx.Platform, ctx.CoreVersion);
            var fileCount = Directory.GetFiles(cdnDir, "*", SearchOption.AllDirectories).Length;

            // 保留云端旧版本资源；本地 PlanVersion 目录同步到 CoreVersion 下。
            var args = $"sync \"{cdnDir}\" \"{cosTarget}/\" --recursive --force";

            var (exitCode, stdout, stderr) = BuildUtils.RunCoscli(_coscliPath, args);

            if (exitCode == 0)
            {
                Debug.Log($"[COS] 上传成功 ({fileCount} 文件)\n{stdout}");
                return true;
            }

            Debug.LogError($"[COS] 上传失败 (exit {exitCode})\nstdout: {stdout}\nstderr: {stderr}");
            return false;
        }

        /// <returns>(线上版本, 冲突消息)。冲突消息为 null 表示无冲突。</returns>
        (string liveVersion, string message) CheckLiveVersion(BuildContext ctx)
        {
            var bootConfig = ReleaseProject.LoadBootConfig();
            var configServerUrl = bootConfig != null
                ? bootConfig.GetConfigServerUrl()
                : null;
            var liveVersion = EditorConfigService.FetchLivePlanVersion(
                configServerUrl, ctx.Platform);

            if (liveVersion == null)
            {
                Debug.LogWarning(
                    "[CloudUpload] 无法获取线上版本（ConfigServer 不可达或后台未配置此平台），" +
                    "跳过版本回退检查，继续上传。首次部署或网络临时异常时属正常。");
                return (null, null);
            }

            if (BuildUtils.IsVersionGreaterOrEqual(liveVersion, ctx.PlanVersion))
            {
                var msg = $"Plan Version {ctx.PlanVersion} <= 线上版本 {liveVersion}，拒绝上传。" +
                          "修复请递增 PlanVersion，回退请在 ConfigServer 切换版本";
                return (liveVersion, msg);
            }

            return (liveVersion, null);
        }
    }
}
