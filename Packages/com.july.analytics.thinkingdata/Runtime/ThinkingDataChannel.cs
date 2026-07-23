using System;
using System.Collections.Generic;
using ThinkingData.Analytics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Analytics
{
    public sealed class ThinkingDataOptions
    {
        public string AppId { get; }
        public string ServerUrl { get; }
        public bool IsProduction { get; set; }
        public bool EnableLog { get; set; }
        public bool EnableAutoTrack { get; set; } = true;
        public bool ForwardUnityErrors { get; set; }
        public string UnityErrorEventName { get; set; } = "UnityLogInfo";

        public ThinkingDataOptions(string appId, string serverUrl)
        {
            AppId = appId ?? throw new ArgumentNullException(nameof(appId));
            ServerUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        }
    }

    /// <summary>Strongly typed ThinkingData SDK channel.</summary>
    public sealed class ThinkingDataChannel : IAnalyticsChannel
    {
        private static readonly string[] SdkObjectNames =
            { "ThinkingData", "ThinkingSDKTask", "ThinkingSDKAutoTrack" };

        private readonly ThinkingDataOptions _options;
        private bool _initialized;
        private bool _forwardingError;

        public ThinkingDataChannel(ThinkingDataOptions options) =>
            _options = options ?? throw new ArgumentNullException(nameof(options));

        public void Initialize()
        {
            if (_initialized) return;
            if (string.IsNullOrWhiteSpace(_options.AppId))
                throw new InvalidOperationException("ThinkingData AppId cannot be empty.");
            if (string.IsNullOrWhiteSpace(_options.ServerUrl))
                throw new InvalidOperationException("ThinkingData ServerUrl cannot be empty.");

            var config = new TDConfig(_options.AppId, _options.ServerUrl)
            {
                mode = _options.IsProduction ? TDMode.Normal : TDMode.Debug,
                timeZone = TDTimeZone.Asia_Shanghai,
            };
            TDAnalytics.Init(config);

            if (_options.EnableAutoTrack)
            {
                TDAnalytics.EnableAutoTrack(
                    TDAutoTrackEventType.AppInstall |
                    TDAutoTrackEventType.AppStart |
                    TDAutoTrackEventType.AppEnd);
            }

            TDAnalytics.EnableLog(_options.EnableLog);
#if !UNITY_EDITOR
            if (_options.ForwardUnityErrors)
                Application.logMessageReceived += OnLogMessageReceived;
#endif
            _initialized = true;
        }

        public void Track(string eventName, Dictionary<string, object> parameters)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(eventName)) return;
            TDAnalytics.Track(eventName, parameters ?? new Dictionary<string, object>());
        }

        public void SetUserId(string userId)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(userId)) return;
            TDAnalytics.Login(userId);
            Flush();
        }

        public void SetUserProperties(Dictionary<string, object> properties)
        {
            if (!_initialized || properties == null) return;
            TDAnalytics.UserSet(properties);
            Flush();
        }

        public void Flush()
        {
            if (_initialized) TDAnalytics.Flush();
        }

        public void SetLogEnabled(bool enabled)
        {
            if (_initialized) TDAnalytics.EnableLog(enabled);
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            Application.logMessageReceived -= OnLogMessageReceived;
            Flush();
            foreach (var objectName in SdkObjectNames)
            {
                var gameObject = GameObject.Find(objectName);
                if (gameObject != null) Object.Destroy(gameObject);
            }
            _initialized = false;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (_forwardingError ||
                type != LogType.Error && type != LogType.Exception)
                return;

            _forwardingError = true;
            try
            {
                Track(_options.UnityErrorEventName, new Dictionary<string, object>(1)
                {
                    { "content", $"{type}\n{condition}\n{stackTrace}" }
                });
            }
            finally
            {
                _forwardingError = false;
            }
        }
    }
}
