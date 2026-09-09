using System.IO;
using System.Linq;
using July.Release;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace July.Release.Editor
{
    /// <summary>
    /// 将微信小游戏首包 data 文件（*.webgl.data.unityweb.bin.br）上传至腾讯云 COS。
    /// 仅在微信平台执行；其他平台 Validate 通过、Execute 跳过。
    /// </summary>
    public sealed class DataFileUploadStep : BuildStep
    {
        public override string Name => "上传微信首包 data";

        string _bucket, _pathPrefix, _coscliPath;

        public override string Validate(BuildContext ctx)
        {
            if (ctx.Platform != PlatformKeys.WeChat)
                return null;

            return BuildUtils.ValidateCosEnvironment(ctx, out _bucket, out _, out _pathPrefix, out _coscliPath);
        }

        public override bool Execute(BuildContext ctx)
        {
            if (ctx.Platform != PlatformKeys.WeChat)
            {
                Debug.Log("[DataFileUpload] 非微信平台，跳过首包 data 上传");
                return true;
            }

            var filePath = ResolveDataFilePath(ctx);
            if (filePath == null)
                return false;

            var fileName = Path.GetFileName(filePath);
            var cosObject = BuildUtils.BuildCosObjectUrl(_bucket, _pathPrefix,
                ctx.Env, ctx.Platform, ctx.CoreVersion, fileName);

            var args = $"cp \"{filePath}\" \"{cosObject}\"";

            var (exitCode, stdout, stderr) = BuildUtils.RunCoscli(_coscliPath, args);

            if (exitCode == 0)
            {
                var length = new FileInfo(filePath).Length;
                var cdnUrl =
                    $"{ctx.CdnUrl.TrimEnd('/')}/{ctx.Env}/{ctx.Platform}/{ctx.CoreVersion}/{fileName}";
                Debug.Log(
                    $"[DataFileUpload] 上传成功\nCDN: {cdnUrl}\n大小: {BuildUtils.FormatSize(length)}\n{stdout}");
                return true;
            }

            Debug.LogError($"[DataFileUpload] 上传失败 (exit {exitCode})\nstdout: {stdout}\nstderr: {stderr}");
            return false;
        }

        /// <summary>
        /// 优先使用 <see cref="BuildContext.DataFilePath"/>；CI 单步重跑时在 WX 导出目录下搜索 data 文件。
        /// </summary>
        static string ResolveDataFilePath(BuildContext ctx)
        {
            var path = ctx.DataFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ctx.Artifacts ??= ReleaseProject.PlatformBuilder(ctx.Platform).LocateArtifacts(ctx);
                path = ctx.Artifacts.DataFilePath;
            }
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            Debug.LogError("[DataFileUpload] No WeChat data artifact was found. Build the player first.");
            return null;
        }
    }
}
