using System;
using System.IO;
using July.Build;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>Unity 构建结果，不代表平台后台上传或整个 Jenkins 发布成功。</summary>
    [Serializable]
    public sealed class ReleaseBuildReport
    {
        public const string FileName = "release-build-result.json";
        public int schemaVersion = 1;
        public bool succeeded;
        public string buildType, platform, buildTarget, environment, coreVersion, planVersion;
        public bool debug, cdnUploaded;
        public string packageVersion, packageDirectory, cdnDirectory, cdnUrl, aotBackupPath;
        public string failedStep, error;
        public double elapsedSeconds;

        public static void Clear() => File.Delete(FileName);

        public static ReleaseBuildReport Create(BuildContext context, BuildResult result, string kind, bool upload)
        {
            var package = context.Artifacts?.GameJsPath;
            if (result.Succeeded && context.Artifacts != null && (string.IsNullOrEmpty(package) || !File.Exists(package)))
                throw new InvalidDataException("Platform export succeeded without a game.js artifact");
            return new ReleaseBuildReport
            {
                succeeded = result.Succeeded, buildType = kind,
                platform = context.Platform, buildTarget = context.Target.ToString(), environment = context.Env,
                coreVersion = context.CoreVersion, planVersion = context.PlanVersion, debug = context.Development,
                cdnUploaded = result.Succeeded && upload, packageVersion = context.PackageVersion,
                packageDirectory = package == null ? null : Path.GetDirectoryName(Path.GetFullPath(package)),
                cdnDirectory = context.CdnOutputDir, cdnUrl = context.CdnUrl,
                aotBackupPath = context.SavedAotBackupPath ?? context.AotBackupOutputPath ?? context.AotBackupInputPath,
                failedStep = result.FailedStep, error = result.Error, elapsedSeconds = result.Elapsed.TotalSeconds
            };
        }

        public static void Save(BuildContext context, BuildResult result, string kind, bool upload)
        {
            var report = Create(context, result, kind, upload);
            // Clear() runs before argument parsing. A terminated/failed write cannot expose an old success.
            var temporary = FileName + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(report, true));
            File.Move(temporary, FileName);
        }
    }
}
