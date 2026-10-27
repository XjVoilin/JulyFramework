using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using July.Release;
using July.Release.Editor;
using LitJson;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace July.Analytics.TencentDn.Editor
{
    /// <summary>腾讯 SDK 的文件及 game.js 处理由适配包拥有，Release 只负责按契约调用。</summary>
    public sealed class TencentDnExportProcessor : IReleaseExportProcessor
    {
        private const string OutputFolder = "july-tencent-dn";
        public string Name => "Tencent DN";

        [InitializeOnLoadMethod]
        private static void Register() => ReleaseProject.ExportProcessors.Register(new TencentDnExportProcessor());

        public string Validate(BuildContext context, IReleaseBootConfig bootConfig)
        {
            var config = (bootConfig as ITencentDnConfigProvider)?.TencentDn;
            if (context.Platform != PlatformKeys.WeChat || config == null || !config.Enabled) return null;
            // JS SDK 数据源 ID 为 number，不能把超出精确整数范围的 long 序列化后交给 JS。
            if (config.UserActionSetId <= 0 || config.UserActionSetId > 9007199254740991L)
                return "启用时必须填写可由 JavaScript 精确表示的正整数数据源 ID。";
            if (string.IsNullOrWhiteSpace(config.SecretKey)) return "启用时必须填写 SecretKey。";
            if (config.SecretKey.Length != 32) return "SDK 要求 SecretKey 为 32 位字符串。";
            var root = SourceDirectory();
            foreach (var file in new[] { "bootstrap.js", "dn-sdk-minigame.js" })
                if (!File.Exists(Path.Combine(root, file))) return $"包内缺少 SDK 文件：{file}";
            return null;
        }

        public void Process(BuildContext context, IReleaseBootConfig bootConfig, PlatformBuildArtifacts artifacts)
        {
            if (context.Platform != PlatformKeys.WeChat) return;
            var config = (bootConfig as ITencentDnConfigProvider)?.TencentDn;
            if (config == null || !config.Enabled) return;

            var parameters = JsonMapper.ToJson(new Dictionary<string, object>
            {
                { "user_action_set_id", config.UserActionSetId },
                { "secret_key", config.SecretKey }
            });
            var content = InjectGameJs(File.ReadAllText(artifacts.GameJsPath), parameters);
            // MiniGameBuildStep 已清空并重新导出目录，此处只生成本次启用的 SDK 文件。
            var output = Path.Combine(Path.GetDirectoryName(artifacts.GameJsPath), OutputFolder);
            Directory.CreateDirectory(output);
            var source = SourceDirectory();
            File.Copy(Path.Combine(source, "bootstrap.js"), Path.Combine(output, "bootstrap.js"), true);
            File.Copy(Path.Combine(source, "dn-sdk-minigame.js"),
                Path.Combine(output, "dn-sdk-minigame.js"), true);
            File.WriteAllText(artifacts.GameJsPath, content, new UTF8Encoding(false));
            Debug.Log("[TencentDn] 导出处理完成");
        }

        private static string SourceDirectory() => Path.Combine(
            PackageInfo.FindForAssembly(typeof(TencentDnExportProcessor).Assembly).resolvedPath, "SDK~");

        private static string InjectGameJs(string source, string configJson)
        {
            const string importAnchor = "import './weapp-adapter';";
            const string startAnchor = "checkVersion().then(";
            var importIndex = RequireUnique(source, importAnchor);
            RequireUnique(source, startAnchor);
            var importLineEnd = source.IndexOf('\n', importIndex + importAnchor.Length);
            if (importLineEnd < 0)
                throw new InvalidDataException("[TencentDn] 微信 game.js 的适配器 import 行缺少换行。");
            var newline = source[importLineEnd - 1] == '\r' ? "\r\n" : "\n";
            source = source.Insert(importLineEnd + 1,
                $"import {{ initializeTencentDn }} from './{OutputFolder}/bootstrap.js';{newline}");
            // managerConfig 已就绪，尚未开始版本检查和 Unity 启动；保留 startGame 预下载锚点。
            return source.Insert(source.IndexOf(startAnchor, StringComparison.Ordinal),
                $"initializeTencentDn(Object.assign({configJson}, {{ appid: managerConfig.APPID }}));{newline}");
        }

        private static int RequireUnique(string source, string anchor)
        {
            var index = source.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0 || source.IndexOf(anchor, index + anchor.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException($"[TencentDn] 微信 game.js 模板锚点缺失或重复：{anchor}");
            return index;
        }
    }
}
