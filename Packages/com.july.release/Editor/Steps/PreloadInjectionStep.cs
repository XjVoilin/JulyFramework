using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using July.Release;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// FullBuild 专用：读取 YooAsset 构建产物 → 生成 preload.json → 注入 game.js 预下载逻辑。
    /// 在 MiniGameBuildStep 之后执行。
    /// <para>
    /// preload.json 中使用完整 CDN URL，避免插件按相对路径自动拼接 StreamingAssets 前缀。
    /// preload.json 上传到 CoreVersion 级目录，HotUpdate 时由 PreloadJsonUpdateStep 覆盖。
    /// </para>
    /// </summary>
    public sealed class PreloadInjectionStep : BuildStep
    {
        public override string Name => "预下载注入";
        public bool UploadEnabled { get; }
        public PreloadInjectionStep() : this(true) { }
        public PreloadInjectionStep(bool upload) => UploadEnabled = upload;

        const string WeChatStartGameMarker = "gameManager.startGame();";
        const string TikTokMainMarker = "main();";
        const string Sentinel = "[PreloadInjection]";

        public override string Validate(BuildContext ctx) => string.IsNullOrEmpty(ctx.CloudUrl)
            ? null
            : BuildUtils.ValidateResourceUrls(ctx, out _, out _, out _);

        public override bool Execute(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CloudUrl))
            {
                Debug.Log($"{Sentinel} CloudUrl 未配置，跳过");
                return true;
            }

            if (string.IsNullOrEmpty(ctx.CdnUrl))
            {
                Debug.LogError($"{Sentinel} CdnUrl 未配置（BootConfig.cdnUrl），无法生成预下载 URL");
                return false;
            }

            var hasBundles = !string.IsNullOrEmpty(ctx.CdnOutputDir) && Directory.Exists(ctx.CdnOutputDir);
            if (hasBundles)
            {
                var bundleUrls = PreloadHelper.CollectBundleUrls(ctx);
                if (bundleUrls.Count > 0)
                {
                    var localPath = PreloadHelper.WritePreloadJson(ctx, bundleUrls);
                    if (UploadEnabled && !PreloadHelper.UploadPreloadJson(ctx, localPath))
                        return false;
                    Debug.Log($"{Sentinel} preload.json 已生成{(UploadEnabled ? "并上传" : "（仅本地）")}, {bundleUrls.Count} URLs");
                }
            }
            else
            {
                Debug.Log($"{Sentinel} 无 AB 产物，跳过 preload.json 生成（使用 CDN 上已有版本）");
            }

            var preloadJsonUrl = PreloadHelper.BuildPreloadJsonUrl(ctx);
            var configServerUrl = ResolveConfigServerUrl(ctx);
            if (!InjectGameJs(ctx, preloadJsonUrl, configServerUrl, ctx.CoreVersion))
                return false;

            Debug.Log($"{Sentinel} game.js 注入完成");
            return true;
        }

        static string ResolveConfigServerUrl(BuildContext ctx)
        {
            var bootConfig = ReleaseProject.LoadBootConfig();
            if (bootConfig == null) return null;
            if (!Enum.TryParse<ReleaseEnvironment>(ctx.Env, true, out var env)) env = bootConfig.env;
            return bootConfig.GetConfigServerUrl(env);
        }

        static bool InjectGameJs(BuildContext ctx, string preloadJsonUrl,
            string configServerUrl, string coreVersion)
        {
            var gameJsPath = PreloadHelper.FindGameJs(ctx);
            if (gameJsPath == null)
                return false;

            var content = File.ReadAllText(gameJsPath, Encoding.UTF8);

            if (content.Contains(Sentinel))
            {
                Debug.Log($"{Sentinel} game.js 已注入，跳过");
                return true;
            }

            var configSnippet = BuildConfigPrefetchSnippet(ctx.Platform, configServerUrl, coreVersion, ctx.Env);

            string injection;
            if (ctx.Platform == PlatformKeys.WeChat)
            {
                if (!content.Contains(WeChatStartGameMarker))
                {
                    Debug.LogError($"{Sentinel} game.js 中未找到 '{WeChatStartGameMarker}'");
                    return false;
                }
                injection = configSnippet + BuildWeChatInjection(preloadJsonUrl);
                content = InjectLaunchDiagnostics(content, ctx.Platform);
                content = content.Replace(WeChatStartGameMarker, injection);
            }
            else if (ctx.Platform == PlatformKeys.TikTok)
            {
                content = InjectLaunchDiagnostics(content, ctx.Platform);
                var lastIdx = content.LastIndexOf(TikTokMainMarker);
                if (lastIdx < 0)
                {
                    Debug.LogError($"{Sentinel} game.js 中未找到 '{TikTokMainMarker}'");
                    return false;
                }
                // 抖音由平台读取动态列表并调度预下载，不在 game.js 中等待网络请求。
                var gameJsonPath = Path.Combine(Path.GetDirectoryName(gameJsPath), "game.json");
                File.WriteAllText(gameJsonPath,
                    ConfigureTikTokPreload(File.ReadAllText(gameJsonPath, Encoding.UTF8), preloadJsonUrl),
                    new UTF8Encoding(false));
                Debug.Log($"{Sentinel} TikTok native preloadDataListUrl={preloadJsonUrl}");
                injection = configSnippet + BuildTikTokInjection(preloadJsonUrl);
                content = content.Substring(0, lastIdx) + injection + content.Substring(lastIdx + TikTokMainMarker.Length);
            }
            else
            {
                Debug.LogWarning($"{Sentinel} 不支持的平台 {ctx.Platform}，跳过注入");
                return true;
            }
            File.WriteAllText(gameJsPath, content, Encoding.UTF8);
            Debug.Log($"{Sentinel} game.js 注入成功 ({ctx.Platform}): {gameJsPath}");
            return true;
        }

        static string InjectLaunchDiagnostics(string content, string platform)
        {
            const string progress = "gameManager.onLaunchProgress((e) => {";
            const string prepared = "gameManager.onModulePrepared(() => {";
            // 导出模板是第三方输入；模板变化时明确中止，避免生成缺少诊断或无法运行的脚本。
            if (!content.Contains(progress) || !content.Contains(prepared))
                throw new InvalidOperationException($"{Sentinel} {platform} game.js 缺少启动阶段回调，请检查 SDK 模板。");

            return content.Replace(progress, progress + "\n" +
                $"console.log('[Preload][{platform}] launchProgress at=' + Date.now() + ' type=' + e.type + ' data=' + JSON.stringify(e.data));\n")
                .Replace(prepared, prepared + "\n" +
                $"console.log('[Preload][{platform}] modulePrepared at=' + Date.now());\n");
        }

        /// <summary>
        /// 生成 config 预拉取 JS 代码（fire-and-forget，不阻塞 startGame/main）。
        /// 响应写入全局 __JULY_CONFIG_CACHE，由 Unity 侧 WebGLConfigCache 读取。
        /// 微信使用 window，抖音使用 GameGlobal（抖音环境无 window 全局对象）。
        /// </summary>
        static string QuoteJavascriptString(string value)
        {
            var writer = new LitJson.JsonWriter { Validate = false };
            writer.Write(value);
            return writer.ToString();
        }

        internal static string BuildConfigPrefetchSnippet(string platform, string configServerUrl, string coreVersion, string environment)
        {
            if (string.IsNullOrEmpty(configServerUrl) || string.IsNullOrEmpty(coreVersion))
                return "";

            var url = configServerUrl.TrimEnd('/') + ClientVersionProtocol.Path;
            var requestFn = platform == PlatformKeys.WeChat ? "wx.request" : "tt.request";
            var globalObj = platform == PlatformKeys.WeChat ? "window" : "GameGlobal";

            return
                $"// {Sentinel} 预取配置（发起后不阻塞启动）\n" +
                $"{requestFn}({{\n" +
                $"    url: {QuoteJavascriptString(url)},\n" +
                "    method: 'POST',\n" +
                $"    data: JSON.stringify({ClientVersionProtocol.RequestJson(coreVersion)}),\n" +
                "    header: {'content-type': 'application/json'},\n" +
                "    timeout: 5000,\n" +
                "    success: function(res) {\n" +
                "        if (res.statusCode === 200 && res.data) {\n" +
                $"            {globalObj}.__JULY_CONFIG_CACHE = JSON.stringify({{\n" +
                $"                requestUrl: {QuoteJavascriptString(url)},\n" +
                $"                environment: {QuoteJavascriptString(environment)},\n" +
                $"                platform: {QuoteJavascriptString(platform)},\n" +
                $"                coreVersion: {QuoteJavascriptString(coreVersion)},\n" +
                "                responseJson: JSON.stringify(res.data)\n" +
                "            });\n" +
                "            console.log('[ConfigPreFetch] cached');\n" +
                "        }\n" +
                "    },\n" +
                "    fail: function(err) { console.warn('[ConfigPreFetch] fail', err.errMsg || err); }\n" +
                "});\n";
        }

        static string BuildWeChatInjection(string preloadJsonUrl) =>
            $"// {Sentinel}\n" +
            "        (function() {\n" +
            "            const startedAt = Date.now();\n" +
           $"            const url = {QuoteJavascriptString(preloadJsonUrl)} + '?t=' + startedAt;\n" +
            "            console.log('[Preload][WeChat] requestStart at=' + startedAt + ' url=' + url);\n" +
            "            wx.request({\n" +
            "                url: url,\n" +
            "                dataType: 'json',\n" +
            "                timeout: 5000,\n" +
            "                success: function(res) {\n" +
            "                    const list = res.data && res.data.list;\n" +
            "                    console.log('[Preload][WeChat] response at=' + Date.now() + ' elapsedMs=' + (Date.now() - startedAt) + ' status=' + res.statusCode + ' count=' + (Array.isArray(list) ? list.length : 'invalid'));\n" +
            "                    if (res.statusCode === 200 && Array.isArray(list) && list.length > 0 && list.every(item => typeof item === 'string' && item.length > 0)) {\n" +
            "                        gameManager.setPreloadList(list);\n" +
            "                        console.log('[Preload][WeChat] listSubmitted at=' + Date.now() + ' count=' + list.length + ' urls=' + JSON.stringify(list));\n" +
            "                    } else {\n" +
            "                        console.warn('[Preload][WeChat] listSkipped: non-200, empty or invalid list; normal resource loading continues');\n" +
            "                    }\n" +
            "                },\n" +
            "                fail: function(err) {\n" +
            "                    console.error('[Preload][WeChat] requestFailed at=' + Date.now() + ' elapsedMs=' + (Date.now() - startedAt), err.errMsg || err);\n" +
            "                },\n" +
            "                complete: function() {\n" +
            "                    console.log('[Preload][WeChat] requestComplete at=' + Date.now() + ' elapsedMs=' + (Date.now() - startedAt));\n" +
            "                }\n" +
            "            });\n" +
            "            console.log('[Preload][WeChat] engineStart at=' + Date.now() + ' elapsedMs=' + (Date.now() - startedAt) + ' mode=parallel-list-request');\n" +
            "            gameManager.startGame();\n" +
            "        })();";

        static string BuildTikTokInjection(string preloadJsonUrl) =>
            $"// {Sentinel}\n" +
            $"console.log('[Preload][TikTok] nativeListConfigured at=' + Date.now() + ' url=' + {QuoteJavascriptString(preloadJsonUrl)});\n" +
            "console.log('[Preload][TikTok] download status is reported by SDK JSFW_PreloadManager logs; configuration is not download confirmation');\n" +
            "console.log('[Preload][TikTok] mainStart at=' + Date.now() + ' mode=native-list-url');\n" +
            "main();\n" +
            "console.log('[Preload][TikTok] mainReturned at=' + Date.now());";

        internal static string ConfigureTikTokPreload(string gameJson, string preloadJsonUrl)
        {
            var config = LitJson.JsonMapper.ToObject(gameJson);
            config["preloadDataListUrl"] = preloadJsonUrl;
            var writer = new LitJson.JsonWriter { PrettyPrint = true };
            LitJson.JsonMapper.ToJson(config, writer);
            return writer.ToString();
        }
    }

    /// <summary>
    /// HotUpdate 专用：重新生成 preload.json 并上传到 COS，覆盖 FullBuild 时的旧版本。
    /// </summary>
    public sealed class PreloadJsonUpdateStep : BuildStep
    {
        public override string Name => "更新预下载列表";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CloudUrl))
                return "CloudUrl 未配置，无法上传 preload.json";
            if (string.IsNullOrEmpty(ctx.CdnUrl))
                return "CdnUrl 未配置（BootConfig.cdnUrl），无法生成预下载 URL";
            return BuildUtils.ValidateResourceUrls(ctx, out _, out _, out _);
        }

        public override bool Execute(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CdnOutputDir) || !Directory.Exists(ctx.CdnOutputDir))
            {
                Debug.LogError("[PreloadJsonUpdate] CdnOutputDir 不存在，AB 构建可能未完成");
                return false;
            }

            var bundleUrls = PreloadHelper.CollectBundleUrls(ctx);
            if (bundleUrls.Count == 0)
            {
                Debug.LogWarning("[PreloadJsonUpdate] 未找到 bundle 文件，跳过");
                return true;
            }

            var localPath = PreloadHelper.WritePreloadJson(ctx, bundleUrls);
            if (!PreloadHelper.UploadPreloadJson(ctx, localPath))
                return false;

            Debug.Log($"[PreloadJsonUpdate] OK, {bundleUrls.Count} URLs");
            return true;
        }
    }

    internal static class PreloadHelper
    {
        const string PreloadJsonName = "preload.json";

        static readonly HashSet<string> ExcludeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".version", ".hash", ".bytes", ".json", ".report"
        };

        static int MaxPreloadCount => ReleaseProject.Profile.MaxPreloadCount;
        static long MaxPreloadBytes => ReleaseProject.Profile.MaxPreloadBytes; // 官方建议 3~5MB

        // 启动必需资源：框架代码标签加项目启动下载标签，与运行时使用同一份规则。
        // 预下载这些包能让启动 / 进大厅时缓存命中，体感秒进。
        static HashSet<string> RequiredTags => new(ReleaseProject.Profile.RequiredPreloadTags, StringComparer.Ordinal);

        public static List<string> CollectBundleUrls(BuildContext ctx)
        {
            // 预下载 URL 写入 preload.json，由小游戏 SDK 在客户端预下载并按 URL 缓存。
            // 必须使用 CDN 加速域名（与 ConfigSnapshot 运行时使用的 cdnUrl 一致），
            // 否则缓存 key 与 YooAsset 实际请求 URL 不一致，预下载缓存命不中。
            var baseUrl = ResourceUrls.PlanRoot(ctx.CdnUrl, ctx.Env, ctx.Platform, ctx.CoreVersion, ctx.PlanVersion);

            // 优先按 manifest 分类（必须 → 非必须）；解析失败则回退到 size-only 的旧逻辑。
            if (TryClassifyByManifest(ctx, out var required, out var optional))
                return SelectPrioritized(baseUrl, required, optional);

            Debug.LogWarning("[Preload] manifest 解析失败，回退到 size-only 选取策略");
            return SelectBySizeOnly(ctx, baseUrl);
        }

        static List<string> SelectPrioritized(string baseUrl,
            List<BundleEntry> required, List<BundleEntry> optional)
        {
            required.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));
            optional.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));

            var requiredTotal = required.Sum(b => b.FileSize);
            var optionalTotal = optional.Sum(b => b.FileSize);
            Debug.Log($"[Preload] 候选: 必须 {required.Count} 个 / {BuildUtils.FormatSize(requiredTotal)}" +
                      $", 非必须 {optional.Count} 个 / {BuildUtils.FormatSize(optionalTotal)}");

            var selected = new List<BundleEntry>();
            long acc = 0;

            int requiredPicked = FillBucket(required, selected, ref acc);
            int optionalPicked = FillBucket(optional, selected, ref acc);

            Debug.Log($"[Preload] 选取 {selected.Count} bundles ({BuildUtils.FormatSize(acc)})" +
                      $" — 必须 {requiredPicked} / 非必须 {optionalPicked}" +
                      $" (上限 {MaxPreloadCount} 个 / {BuildUtils.FormatSize(MaxPreloadBytes)})");

            return selected.Select(b => $"{baseUrl}/{b.FileName}").ToList();
        }

        static int FillBucket(List<BundleEntry> source, List<BundleEntry> selected, ref long acc)
        {
            // 装不下的大包跳过（continue），让后面的小包有机会填进剩余预算。
            int picked = 0;
            foreach (var b in source)
            {
                if (selected.Count >= MaxPreloadCount)
                    break;
                if (acc + b.FileSize > MaxPreloadBytes)
                    continue;
                selected.Add(b);
                acc += b.FileSize;
                picked++;
            }
            return picked;
        }

        static List<string> SelectBySizeOnly(BuildContext ctx, string baseUrl)
        {
            var bundles = new List<(string url, long size)>();
            foreach (var file in Directory.GetFiles(ctx.CdnOutputDir))
            {
                if (ExcludeExtensions.Contains(Path.GetExtension(file)))
                    continue;
                var fileName = Path.GetFileName(file);
                bundles.Add(($"{baseUrl}/{fileName}", new FileInfo(file).Length));
            }

            bundles.Sort((a, b) => b.size.CompareTo(a.size));
            var totalSize = bundles.Sum(b => b.size);
            Debug.Log($"[Preload] (fallback) 全部 {bundles.Count} bundles, {BuildUtils.FormatSize(totalSize)}");

            var selected = new List<(string url, long size)>();
            long acc = 0;
            foreach (var b in bundles)
            {
                if (selected.Count >= MaxPreloadCount) break;
                if (acc + b.size > MaxPreloadBytes && selected.Count > 0) break;
                selected.Add(b);
                acc += b.size;
            }
            Debug.Log($"[Preload] (fallback) 选取 {selected.Count} bundles, {BuildUtils.FormatSize(acc)}");
            return selected.Select(b => b.url).ToList();
        }

        static bool TryClassifyByManifest(BuildContext ctx,
            out List<BundleEntry> required, out List<BundleEntry> optional)
        {
            required = new List<BundleEntry>();
            optional = new List<BundleEntry>();

            var manifestPath = FindManifestJson(ctx);
            if (manifestPath == null)
                return false;

            ManifestDto dto;
            try
            {
                var raw = File.ReadAllText(manifestPath, Encoding.UTF8);
                dto = JsonUtility.FromJson<ManifestDto>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Preload] manifest 反序列化失败: {ex.Message}");
                return false;
            }

            if (dto?.BundleList == null || dto.BundleList.Count == 0)
                return false;

            // 物理文件用作存在性校验，避免 manifest 与磁盘不同步
            var existing = new HashSet<string>(
                Directory.GetFiles(ctx.CdnOutputDir).Select(Path.GetFileName),
                StringComparer.Ordinal);

            int skipUntagged = 0, skipMissing = 0;

            foreach (var b in dto.BundleList)
            {
                if (b.Tags == null || b.Tags.Length == 0)
                {
                    skipUntagged++;
                    continue;
                }

                bool hasRequired = b.Tags.Any(RequiredTags.Contains);

                var fileName = BuildBundleFileName(b.BundleName, b.FileHash, dto.OutputNameStyle);
                if (!existing.Contains(fileName))
                {
                    skipMissing++;
                    continue;
                }

                var entry = new BundleEntry { FileName = fileName, FileSize = b.FileSize };
                if (hasRequired) required.Add(entry);
                else optional.Add(entry);
            }

            Debug.Log($"[Preload] manifest 分类 → 必须 {required.Count} / 非必须 {optional.Count}" +
                      $" (跳过: 无 Tag {skipUntagged}, 物理缺失 {skipMissing})");
            return required.Count > 0 || optional.Count > 0;
        }

        static string FindManifestJson(BuildContext ctx)
        {
            // YooAssetSettingsData.GetManifestJsonFileName 返回的文件名格式：{PackageName}_{PackageVersion}.json
            var preferred = Path.Combine(ctx.CdnOutputDir,
                $"{BuildUtils.PackageName}_{ctx.PackageVersion}.json");
            if (File.Exists(preferred))
                return preferred;

            // 兜底：CdnOutputDir 下匹配 {PackageName}_*.json
            var matches = Directory.GetFiles(ctx.CdnOutputDir, $"{BuildUtils.PackageName}_*.json");
            if (matches.Length == 1)
                return matches[0];
            if (matches.Length > 1)
            {
                Debug.LogWarning($"[Preload] 找到多个 manifest JSON：{string.Join(", ", matches.Select(Path.GetFileName))}");
                return matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
            }
            Debug.LogWarning($"[Preload] 未找到 manifest JSON: {preferred}");
            return null;
        }

        // 对应 EFileNameStyle：0=HashName / 1=BundleName / 2=BundleName_HashName。
        // 项目使用 BundleName_HashName，参考 ManifestTools.GetRemoteBundleFileName。
        static string BuildBundleFileName(string bundleName, string fileHash, int nameStyle)
        {
            const int HashName = 0;
            const int BundleName = 1;
            const int BundleNameHashName = 2;

            switch (nameStyle)
            {
                case HashName:
                    return $"{fileHash}{Path.GetExtension(bundleName)}";
                case BundleName:
                    return bundleName;
                case BundleNameHashName:
                {
                    var ext = Path.GetExtension(bundleName);
                    if (string.IsNullOrEmpty(ext))
                        return $"{bundleName}_{fileHash}";
                    var stem = bundleName.Substring(0, bundleName.Length - ext.Length);
                    return $"{stem}_{fileHash}{ext}";
                }
                default:
                    Debug.LogWarning($"[Preload] 未知 OutputNameStyle={nameStyle}, 退回 BundleName_HashName");
                    goto case BundleNameHashName;
            }
        }

        [Serializable]
        sealed class ManifestDto
        {
            public int OutputNameStyle;
            public List<BundleDto> BundleList;
        }

        [Serializable]
        sealed class BundleDto
        {
            public string BundleName;
            public string FileHash;
            public long FileSize;
            public string[] Tags;
        }

        sealed class BundleEntry
        {
            public string FileName;
            public long FileSize;
        }

        public static string WritePreloadJson(BuildContext ctx, List<string> urls)
        {
            var dir = Path.GetDirectoryName(ctx.CdnOutputDir)!;
            var path = Path.Combine(dir, PreloadJsonName);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"list\": [");
            for (var i = 0; i < urls.Count; i++)
            {
                sb.Append($"    \"{urls[i]}\"");
                if (i < urls.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.Append('}');

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Preload] preload.json → {path}");
            return path;
        }

        public static string BuildPreloadJsonUrl(BuildContext ctx)
        {
            // 微信请求 / 抖音平台预下载读取同一个大版本列表，必须走 CDN 加速域名。
            return $"{ctx.CdnUrl.TrimEnd('/')}/{ctx.Env}/{ctx.Platform}/{ctx.CoreVersion}/{PreloadJsonName}";
        }

        public static bool UploadPreloadJson(BuildContext ctx, string localPath)
        {
            var err = BuildUtils.ValidateCosEnvironment(ctx,
                out var bucket, out _, out var pathPrefix, out var coscliPath);
            if (err != null)
            {
                Debug.LogError($"[Preload] COS 环境不可用: {err}");
                return false;
            }

            var cosTarget = BuildUtils.BuildCosObjectUrl(bucket, pathPrefix,
                ctx.Env, ctx.Platform, ctx.CoreVersion, PreloadJsonName);
            var (exitCode, stdout, stderr) = BuildUtils.RunCoscli(coscliPath,
                $"cp \"{localPath}\" \"{cosTarget}\"");

            if (exitCode == 0)
            {
                Debug.Log($"[Preload] 上传成功 → {cosTarget}");
                return true;
            }

            Debug.LogError($"[Preload] 上传失败 (exit {exitCode})\nstderr: {stderr}");
            return false;
        }

        public static string FindGameJs(BuildContext ctx)
        {
            ctx.Artifacts ??= ReleaseProject.PlatformBuilder(ctx.Platform).LocateArtifacts(ctx);
            var path = ctx.Artifacts.GameJsPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            Debug.LogError($"[Preload] game.js was not found in {ctx.Artifacts.ExportDirectory}");
            return null;
        }
    }
}
