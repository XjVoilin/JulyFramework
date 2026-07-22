using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace July.Config
{
    public sealed class RemoteConfigFetchOptions
    {
        public int MaxAttempts { get; set; } = 3;
        public int RequestTimeoutSeconds { get; set; } = 15;
        public int InitialRetryDelayMilliseconds { get; set; } = 1000;
        public int MaxRetryDelayMilliseconds { get; set; } = 30_000;

        internal void Validate()
        {
            if (MaxAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
            if (RequestTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(RequestTimeoutSeconds));
            if (InitialRetryDelayMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(InitialRetryDelayMilliseconds));
            if (MaxRetryDelayMilliseconds < InitialRetryDelayMilliseconds)
                throw new ArgumentOutOfRangeException(nameof(MaxRetryDelayMilliseconds));
        }
    }

    public readonly struct RemoteConfigAttemptFailure
    {
        public int Attempt { get; }
        public int MaxAttempts { get; }
        public string Error { get; }
        public string ResponseText { get; }

        public RemoteConfigAttemptFailure(int attempt, int maxAttempts,
            string error, string responseText)
        {
            Attempt = attempt;
            MaxAttempts = maxAttempts;
            Error = error;
            ResponseText = responseText;
        }
    }

    /// <summary>
    /// Reusable remote-config transport and retry policy. The project supplies a response
    /// validator that also maps its own schema; returning null accepts the response.
    /// </summary>
    public static class RemoteConfigFetcher
    {
        public static async UniTask<string> PostJsonUntilAcceptedAsync(
            string url,
            string bodyJson,
            Func<string, string> validateAndApply,
            RemoteConfigFetchOptions options = null,
            Action<RemoteConfigAttemptFailure> onAttemptFailed = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be empty.", nameof(url));
            if (validateAndApply == null)
                throw new ArgumentNullException(nameof(validateAndApply));

            options ??= new RemoteConfigFetchOptions();
            options.Validate();
            string lastError = null;

            for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string responseText = null;
                try
                {
                    responseText = await PostJsonOnceAsync(url, bodyJson,
                        options.RequestTimeoutSeconds, cancellationToken);
                    lastError = validateAndApply(responseText);
                    if (lastError == null)
                        return responseText;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                }

                onAttemptFailed?.Invoke(new RemoteConfigAttemptFailure(
                    attempt, options.MaxAttempts, lastError, responseText));

                if (attempt >= options.MaxAttempts) continue;
                var delay = Math.Min(
                    options.InitialRetryDelayMilliseconds * attempt,
                    options.MaxRetryDelayMilliseconds);
                if (delay > 0)
                    await UniTask.Delay(delay, cancellationToken: cancellationToken);
            }

            throw new InvalidOperationException(
                $"Remote config failed after {options.MaxAttempts} attempts: {lastError}");
        }

        private static async UniTask<string> PostJsonOnceAsync(string url,
            string bodyJson, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(bodyJson ?? string.Empty);
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body)
                {
                    contentType = "application/json"
                },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
            };

            var operation = request.SendWebRequest();
            await UniTask.WaitUntil(() => operation.isDone,
                cancellationToken: cancellationToken);
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(
                    $"HTTP {request.responseCode}: {request.error}");
            return request.downloadHandler.text;
        }
    }
}
