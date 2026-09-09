using System.Text;
using July.Release;
using LitJson;
using UnityEngine;
using UnityEngine.Networking;

namespace July.Release.Editor
{
    /// <summary>
    /// Editor / CI 侧 ConfigServer 访问。
    /// 使用 UnityWebRequest（走原生 HTTP 栈），与运行时 <c>ConfigSnapshot</c> 保持一致：
    ///   - URL = <c>GetConfigServerUrl(env)</c> + <c>/client_version</c>
    ///   - POST JSON，字段 <c>CoreVersion</c>（服务端契约）
    /// 避免 Mono <c>System.Net.Http</c> 在部分服务器 TLS 证书下握手失败。
    /// </summary>
    public static class EditorConfigService
    {
        private const int TimeoutSeconds = 10;
        private const string ClientVersionPath = ClientVersionProtocol.Path;

        /// <summary>
        /// 拉取指定平台的线上 PlanVersion（服务端 JSON 字段名仍是 <c>PlanVersion</c>，此处做内部语义映射）。
        /// 返回 null 表示失败（网络不可达 / 响应格式错误 / 无该平台配置）。
        /// </summary>
        public static string FetchLivePlanVersion(string configServerUrl, string platform)
        {
            var json = FetchConfigJson(configServerUrl);
            if (json == null) return null;

            var root = JsonMapper.ToObject(json);
            var version = ClientVersionProtocol.ReadPlanVersion(root, platform);

            if (string.IsNullOrEmpty(version)) return null;
            return BuildUtils.IsValidVersion(version) ? version : null;
        }

        private static string FetchConfigJson(string configServerUrl)
        {
            if (string.IsNullOrEmpty(configServerUrl))
            {
                Debug.LogWarning("[EditorConfigService] configServerUrl 为空");
                return null;
            }

            var url = configServerUrl.TrimEnd('/') + ClientVersionPath;
            var body = ClientVersionProtocol.RequestJson(Application.version);
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(bodyBytes) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            req.SetRequestHeader("Content-Type", "application/json");

            var op = req.SendWebRequest();
            while (!op.isDone) { /* 同步等待；Editor 面板按钮触发，阻塞 ≤ 10s 可接受 */ }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[EditorConfigService] 请求失败: {req.error} " +
                    $"(url={url}, httpCode={req.responseCode})");
                return null;
            }

            return req.downloadHandler.text;
        }
    }
}
