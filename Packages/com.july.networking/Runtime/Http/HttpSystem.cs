using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace July.Networking
{
    public class HttpSystem : SystemBase, IHttpSystem
    {
        private IHttpHandler _handler;
        private HttpModuleOptions _options = new();
        private Dictionary<string, string> _defaultHeaders;

        private readonly Queue<HttpQueueEntity> _queue = new();
        private bool _isProcessing;
        private bool _isPessimistic;
        private bool _blockingShown;

        private HttpPendingQueueStore _pendingStore;

        public void Configure(HttpModuleOptions options, IHttpHandler handler)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _handler = handler;
            _pendingStore = GetStore<HttpPendingQueueStore>()
                ?? throw new InvalidOperationException("HttpSystem 需要先注册 HttpPendingQueueStore。");
        }

        public void SetDefaultHeader(string key, string value)
        {
            _defaultHeaders ??= new Dictionary<string, string>();
            _defaultHeaders[key] = value;
        }

        public void RemoveDefaultHeader(string key) => _defaultHeaders?.Remove(key);

        #region Send — Direct Path

        public async UniTask SendAsync(HttpEntity entity, CancellationToken ct = default)
        {
            var maxRetry = entity.MaxRetryCount >= 0 ? entity.MaxRetryCount : _options.MaxRetryCount;
            var retryCount = 0;

            while (true)
            {
                await SendInternal(entity, ct);
                if (entity.Code != HttpEntityBase.CodeNetworkError) break;
                if (retryCount >= maxRetry) break;

                var delay = _options.CalculateRetryDelay(retryCount);
                await UniTask.Delay(delay, cancellationToken: ct);
                retryCount++;
            }

            if (CheckKick(entity)) return;

            if (!entity.IsOk && _options.ReLoginCode != 0 && entity.Code == _options.ReLoginCode)
            {
                if (_handler != null && await _handler.OnReLoginRequired(ct))
                {
                    await SendInternal(entity, ct);
                    if (CheckKick(entity)) return;
                }
            }

            if (!entity.IsOk) _handler?.OnError(entity.Code, entity.Msg);
        }

        #endregion

        #region Send — Queue Path

        public void Send(HttpQueueEntity entity)
        {
            if (entity.IsOptimistic)
            {
                try { entity.ApplyLocal(); }
                catch (Exception ex)
                {
                    JLogger.LogError($"[HttpSystem] ApplyLocal exception: {ex.Message}");
                    entity.SetCompleted();
                    return;
                }
            }

            _pendingStore.Enqueue(entity.Path, entity.BuildBody());

            if (!entity.IsOptimistic && !_isPessimistic)
            {
                _isPessimistic = true;
                SetBlocking(true);
            }

            _queue.Enqueue(entity);
            if (!_isProcessing) ProcessQueueAsync().Forget();
        }

        private async UniTask ProcessQueueAsync()
        {
            _isProcessing = true;
            try
            {
                while (_queue.Count > 0)
                {
                    var entity = _queue.Dequeue();
                    var removePending = true;
                    try
                    {
                        await SendWithRetry(entity);
                        if (CheckKick(entity)) { removePending = false; return; }

                        if (!entity.IsOk && _options.ReLoginCode != 0 && entity.Code == _options.ReLoginCode)
                        {
                            var success = _handler != null && await _handler.OnReLoginRequired(default);
                            if (!success) { removePending = false; return; }

                            entity.RegenerateRequestId();
                            await SendWithRetry(entity);
                            if (CheckKick(entity)) { removePending = false; return; }
                        }

                        DispatchResult(entity);
                    }
                    catch (OperationCanceledException) { removePending = false; return; }
                    finally
                    {
                        if (removePending)
                            _pendingStore.Dequeue();
                        entity.SetCompleted();
                    }
                }
            }
            finally
            {
                ClearQueue();
                _isProcessing = false;
                _isPessimistic = false;
                SetBlocking(false);
            }
        }

        private void DispatchResult(HttpQueueEntity entity)
        {
            if (entity.IsOk) { try { entity.OnResponse(); } catch { } return; }
            if (entity.Code < 0) { _handler?.OnError(entity.Code, entity.Msg); return; }
            try { entity.OnError(); } catch { }
            _handler?.OnError(entity.Code, entity.Msg);
        }

        private async UniTask SendWithRetry(HttpQueueEntity entity)
        {
            var retryCount = 0;
            while (true)
            {
                await SendInternal(entity, default);
                if (entity.Code != HttpEntityBase.CodeNetworkError) break;
                retryCount++;

                if (_isPessimistic && retryCount >= _options.MaxRetryCount)
                {
                    var shouldContinue = _handler != null && await _handler.OnRetryExceeded();
                    if (!shouldContinue) break;
                    retryCount = 0;
                    continue;
                }

                var delay = _options.CalculateRetryDelay(retryCount - 1);
                await UniTask.Delay(delay);
            }
        }

        #endregion

        public bool HasPendingEntries()
            => _pendingStore != null && _pendingStore.HasEntries;

        #region Replay

        private class ReplayEntry : HttpEntityBase
        {
            public override string Path { get; }
            private readonly string _body;
            internal ReplayEntry(string path, string body) { Path = path; _body = body; }
            protected internal override string BuildBody() => _body;
            protected override void SetResponseData(string dataJson) { }
        }

        public UniTask ReplayPendingAsync()
        {
            if (_pendingStore == null || !_pendingStore.HasEntries)
                return UniTask.CompletedTask;
            return ReplayPendingCoreAsync();
        }

        private async UniTask ReplayPendingCoreAsync()
        {
            while (_pendingStore.TryPeek(out var entry))
            {
                var entity = new ReplayEntry(entry.Path, entry.Body);
                var retryCount = 0;

                while (true)
                {
                    await SendInternal(entity, default);
                    if (entity.Code != HttpEntityBase.CodeNetworkError) break;
                    var delay = _options.CalculateRetryDelay(retryCount);
                    await UniTask.Delay(delay);
                    retryCount++;
                }

                _pendingStore.Dequeue();
            }
        }

        #endregion

        #region Internal

        private void ClearQueue()
        {
            while (_queue.Count > 0) _queue.Dequeue().SetCompleted();
        }

        private void SetBlocking(bool show)
        {
            if (show == _blockingShown) return;
            _blockingShown = show;
            _handler?.OnBlockingChanged(show);
        }

        private bool CheckKick(HttpEntityBase entity)
        {
            if (_options.KickCode == 0 || entity.IsOk || entity.Code != _options.KickCode) return false;
            _handler?.OnKicked();
            return true;
        }

        private async UniTask SendInternal(HttpEntityBase entity, CancellationToken ct)
        {
            var bodyJson = entity.BuildBody();
            var url = BuildUrl(entity.Path);
            byte[] bodyBytes = null;
            var method = "GET";

            if (bodyJson != null)
            {
                bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
                method = "POST";
            }

            var tag = entity.LogTag ?? entity.Path;
            JLogger.Log($"[HTTP] >>> {method} {url} {tag}" +
                      (bodyJson != null ? $"\n{bodyJson}" : ""));

            var response = await SendRawAsync(url, method, bodyBytes, _defaultHeaders, _options.TimeoutSeconds, ct);

            if (response.IsNetworkError)
            {
                entity.Code = HttpEntityBase.CodeNetworkError;
                entity.Msg = response.Error ?? "Network error";
                JLogger.LogWarning($"[HTTP] <<< NETWORK ERROR {tag}: {entity.Msg}");
                return;
            }

            var text = response.GetText();
            JLogger.Log($"[HTTP] <<< {response.StatusCode} {tag}" +
                      (!string.IsNullOrEmpty(text) ? $"\n{text}" : ""));

            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    entity.ParseResponse(text);
                    return;
                }
                catch (Exception ex)
                {
                    if (response.IsHttpOk)
                    {
                        entity.Code = HttpEntityBase.CodeDataProcessingError;
                        entity.Msg = $"Data processing failed: {ex.Message}";
                        JLogger.LogError($"[HTTP] <<< DATA ERROR {tag}: {entity.Msg}");
                        return;
                    }
                }
            }

            entity.Code = HttpEntityBase.CodeHttpError;
            entity.Msg = response.Error ?? $"HTTP {response.StatusCode}";
            JLogger.LogWarning($"[HTTP] <<< HTTP ERROR {tag}: {entity.Msg}");
        }

        private async UniTask<HttpResponse> SendRawAsync(string url, string method, byte[] body,
            Dictionary<string, string> headers, int timeoutSeconds, CancellationToken ct)
        {
            using var request = new UnityWebRequest(url, method);
            request.timeout = timeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();

            if (body != null)
                request.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };

            if (headers != null)
            {
                foreach (var kvp in headers)
                    request.SetRequestHeader(kvp.Key, kvp.Value);
            }

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            var isNetworkError = request.result == UnityWebRequest.Result.ConnectionError;
            return new HttpResponse(
                (int)request.responseCode,
                request.downloadHandler?.data,
                request.error,
                isNetworkError);
        }

        private string BuildUrl(string path)
        {
            if (string.IsNullOrEmpty(_options.BaseUrl) || path.StartsWith("http"))
                return path;
            return _options.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        #endregion
    }
}
