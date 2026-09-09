using July.Persistence;
using July.Config;
using July.Logging;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

namespace July.Release
{
    /// <summary>
    /// 运行时配置快照 —— 启动阶段独立拉取 Config Server 配置。
    ///
    /// 版本语义（见 spec §2.2）：
    ///   Application.version   == CoreVersion（主包二进制版本）
    ///   PlanVersion           == 服务端返回的资源/规划版本，决定 CDN 资源路径
    ///
    /// 请求体 JSON 字段 `CoreVersion` 保持不变（服务端契约），实际承载 CoreVersion。
    /// 响应字段 `platforms.{platform}.PlanVersion` 保持不变（服务端契约），映射到 PlanVersion。
    /// </summary>
    public class ReleaseConfigSnapshot
    {
        private const int MaxRetryDelayMs = 30_000;
        private const int RequestTimeoutSeconds = 15;
        private const string VersionOverrideKey = "debug_plan_version_override";
        private const string EnvOverrideKey = "debug_env_override";

        private readonly ReleaseEnvironment _env;
        private readonly string _envName;
        private readonly string _platformKey;
        private readonly string _cdnUrl;
        private readonly string _configServerUrl;

        public string ServerUrl { get; private set; }

        public string PlanVersion { get; private set; }

        public bool IsAudit { get; private set; }
        public bool IsLoaded { get; private set; }
        public ReleaseEnvironment Env => _env;
        public bool IsDev => _env == ReleaseEnvironment.Dev;

        public ReleaseConfigSnapshot(ReleaseEnvironment environment, string cdnUrl,
            Func<ReleaseEnvironment, string> configServerUrl, string platform)
        {
            _env = ResolveEnv(environment);
            _envName = _env.ToString();
            _platformKey = platform;
            _cdnUrl = cdnUrl;
            _configServerUrl = configServerUrl(_env);
        }

        /// <summary>
        /// 使用本地默认值填充，不发起网络请求。Editor 本地模式下使用，保证 ConfigSnapshot 永远有完整数据。
        /// </summary>
        public void FillDefaults()
        {
            ServerUrl = _configServerUrl;
            PlanVersion = Application.version;
            IsAudit = false;
        }

        /// <summary>
        /// 请求配置服务器，重试 maxAttempts 次直到成功。
        /// </summary>
        public async UniTask FetchAsync(int maxAttempts = 3,
            CancellationToken cancellationToken = default)
        {
            var url = _configServerUrl.TrimEnd('/') + ClientVersionProtocol.Path;
            // 请求体字段 "CoreVersion" 是服务端契约名，值是 CoreVersion（即 Application.version）
            var bodyJson = ClientVersionProtocol.RequestJson(Application.version);

            var options = new RemoteConfigFetchOptions
            {
                MaxAttempts = maxAttempts,
                RequestTimeoutSeconds = RequestTimeoutSeconds,
                InitialRetryDelayMilliseconds = 1000,
                MaxRetryDelayMilliseconds = MaxRetryDelayMs,
            };
            await RemoteConfigFetcher.PostJsonUntilAcceptedAsync(
                url,
                bodyJson,
                TryParse,
                options,
                failure =>
                {
                    var kind = failure.ResponseText == null ? "请求失败" : "响应解析失败";
                    JLogger.LogWarning(
                        $"[ConfigSnapshot] {kind}（第 {failure.Attempt}/{failure.MaxAttempts} 次）: " +
                        $"{failure.Error}");
                },
                cancellationToken);

            JLogger.Log($"[ConfigSnapshot] {url}\n 配置加载成功 — env={_envName} platform={_platformKey} planVersion={PlanVersion} isAudit={IsAudit}");
            IsLoaded = true;
            ApplyDebugOverride();
        }

        /// <returns>null 表示成功；非 null 为错误描述。</returns>
        private string TryParse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return "响应为空";

            var root = JsonMapper.ToObject(json);

            var serverUrl = root.GetString("serverUrl");
            if (string.IsNullOrEmpty(serverUrl))
                return "缺少 serverUrl 字段";

            var platforms = root.GetObject("platforms");
            if (platforms == null)
                return "缺少 platforms 字段";

            var platformData = platforms.GetObject(_platformKey);
            if (platformData == null)
                return $"platforms 中未找到 \"{_platformKey}\" 配置";

            var planVersion = platformData.GetString(ClientVersionProtocol.PlanVersionField);
            if (string.IsNullOrEmpty(planVersion))
                return $"platforms.{_platformKey} 缺少 PlanVersion 字段";

            ServerUrl = serverUrl;
            PlanVersion = planVersion;
            IsAudit = platformData.GetBool("isAudit");
            return null;
        }

        /// <summary>
        /// 尝试使用 game.js 预拉取的缓存 JSON 填充配置。
        /// 解析失败或 Debug 环境覆写导致 env 不匹配时返回 false，调用方应走正常 FetchAsync。
        /// </summary>
        public bool TryApplyCached(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            var error = TryParse(json);
            if (error != null)
            {
                JLogger.LogWarning($"[ConfigSnapshot] JS 缓存解析失败: {error}");
                return false;
            }

            IsLoaded = true;
            ApplyDebugOverride();
            JLogger.Log($"[ConfigSnapshot] 使用 JS 预拉取缓存 — env={_envName} platform={_platformKey} planVersion={PlanVersion} isAudit={IsAudit}");
            return true;
        }

        #region CDN URL

        /// <summary>
        /// 拼接 CDN 主 URL，路径约定：{cdn}/{env}/{platform}/{CoreVersion}/{PlanVersion}
        /// Application.version == CoreVersion（FullBuild 时 Jenkins 注入）。
        /// </summary>
        public string GetRemoteMainURL()
        {
            return ResourceUrls.PlanRoot(_cdnUrl, _envName, _platformKey, Application.version, PlanVersion);
        }

        #endregion

        #region Debug 环境覆写

        /// <summary>
        /// Debug 包可通过 PlayerPrefs 覆写环境，下次启动生效。Release 包中此方法不存在。
        /// </summary>
        public static void SetEnvOverride(ReleaseEnvironment? env)
        {
#if JULYGF_DEBUG
            if (env == null)
            {
                PlatformPreferences.DeleteKey(EnvOverrideKey);
                JLogger.Log("[ConfigSnapshot] 已清除环境覆写");
            }
            else
            {
                PlatformPreferences.SetInt(EnvOverrideKey, (int)env.Value);
                JLogger.Log($"[ConfigSnapshot] 已设置环境覆写: {env.Value}，重启后生效");
            }

            PlatformPreferences.Save();
#endif
        }

        private static ReleaseEnvironment ResolveEnv(ReleaseEnvironment bootEnv)
        {
#if JULYGF_DEBUG
            var ov = PlatformPreferences.GetInt(EnvOverrideKey, -1);
            if (ov >= 0 && Enum.IsDefined(typeof(ReleaseEnvironment), ov))
            {
                var overrideEnv = (ReleaseEnvironment)ov;
                JLogger.Log($"[ConfigSnapshot] 使用环境覆写: {overrideEnv}（BootConfig: {bootEnv}）");
                return overrideEnv;
            }
#endif
            return bootEnv;
        }

        #endregion

        #region Debug 版本覆写

        public static void SetVersionOverride(string planVersion)
        {
#if JULYGF_DEBUG
            if (string.IsNullOrEmpty(planVersion))
            {
                PlatformPreferences.DeleteKey(VersionOverrideKey);
                JLogger.Log("[ConfigSnapshot] 已清除本地 PlanVersion 覆写");
            }
            else
            {
                PlatformPreferences.SetString(VersionOverrideKey, planVersion);
                JLogger.Log($"[ConfigSnapshot] 已设置本地 PlanVersion 覆写: {planVersion}，重启后生效");
            }

            PlatformPreferences.Save();
#endif
        }

        public static string GetVersionOverride()
        {
#if JULYGF_DEBUG
            var v = PlatformPreferences.GetString(VersionOverrideKey, string.Empty);
            return string.IsNullOrEmpty(v) ? null : v;
#else
            return null;
#endif
        }

        private void ApplyDebugOverride()
        {
#if JULYGF_DEBUG
            var overrideVersion = GetVersionOverride();
            if (overrideVersion != null)
            {
                JLogger.Log($"[ConfigSnapshot] 使用本地覆写 PlanVersion: {overrideVersion}（服务器版本: {PlanVersion}）");
                PlanVersion = overrideVersion;
            }
#endif
        }

        #endregion
    }
}
