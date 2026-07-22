using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Analytics
{
    public sealed class ThinkingDataOptions
    {
        public string AppId { get; }
        public string ServerUrl { get; }
        public bool IsProduction { get; set; }
        public bool EnableAutoTrack { get; set; } = true;
        public bool ForwardUnityErrors { get; set; }
        public string UnityErrorEventName { get; set; } = "UnityLogInfo";

        public ThinkingDataOptions(string appId, string serverUrl)
        {
            AppId = appId ?? throw new ArgumentNullException(nameof(appId));
            ServerUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        }
    }

    /// <summary>
    /// Optional ThinkingData adapter. Reflection keeps the analytics package usable when the
    /// third-party SDK is absent while still validating its required runtime surface.
    /// </summary>
    public sealed class ThinkingDataChannel : IAnalyticsChannel
    {
        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly string[] SdkObjectNames =
            { "ThinkingData", "ThinkingSDKTask", "ThinkingSDKAutoTrack" };

        private readonly ThinkingDataOptions _options;
        private Type _analyticsType;
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

            _analyticsType = RequireType("ThinkingData.Analytics.TDAnalytics");
            var configType = RequireType("ThinkingData.Analytics.TDConfig");
            var config = Activator.CreateInstance(
                configType, _options.AppId, _options.ServerUrl);

            SetMember(configType, config, "mode", ParseEnum(
                "ThinkingData.Analytics.TDMode",
                _options.IsProduction ? "Normal" : "Debug"));
            SetMember(configType, config, "timeZone", ParseEnum(
                "ThinkingData.Analytics.TDTimeZone", "Asia_Shanghai"));
            Invoke("Init", config);

            if (_options.EnableAutoTrack)
            {
                var autoTrack = ParseEnum(
                    "ThinkingData.Analytics.TDAutoTrackEventType",
                    "AppInstall, AppStart, AppEnd");
                Invoke("EnableAutoTrack", autoTrack);
            }

            Invoke("EnableLog", false);
#if !UNITY_EDITOR
            if (_options.ForwardUnityErrors)
                Application.logMessageReceived += OnLogMessageReceived;
#endif
            _initialized = true;
        }

        public void Track(string eventName, Dictionary<string, object> parameters)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(eventName)) return;
            Invoke("Track", eventName, parameters ?? new Dictionary<string, object>());
        }

        public void SetUserId(string userId)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(userId)) return;
            Invoke("Login", userId);
            Flush();
        }

        public void SetUserProperties(Dictionary<string, object> properties)
        {
            if (!_initialized || properties == null) return;
            Invoke("UserSet", properties);
            Flush();
        }

        public void Flush()
        {
            if (_initialized) Invoke("Flush");
        }

        public void SetLogEnabled(bool enabled)
        {
            if (_initialized) Invoke("EnableLog", enabled);
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
            _analyticsType = null;
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

        private void Invoke(string methodName, params object[] arguments)
        {
            var method = _analyticsType.GetMethods(StaticFlags)
                .Where(candidate => candidate.Name == methodName)
                .FirstOrDefault(candidate => ParametersMatch(
                    candidate.GetParameters(), arguments));
            if (method == null)
                throw new MissingMethodException(_analyticsType.FullName, methodName);
            method.Invoke(null, arguments);
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] arguments)
        {
            if (parameters.Length != arguments.Length) return false;
            for (var index = 0; index < parameters.Length; index++)
            {
                var argument = arguments[index];
                if (argument != null &&
                    !parameters[index].ParameterType.IsInstanceOfType(argument))
                    return false;
            }
            return true;
        }

        private static Type RequireType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            throw new InvalidOperationException(
                $"ThinkingData SDK type '{fullName}' is unavailable.");
        }

        private static object ParseEnum(string typeName, string value) =>
            Enum.Parse(RequireType(typeName), value);

        private static void SetMember(Type type, object instance, string memberName,
            object value)
        {
            var field = type.GetField(memberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            var property = type.GetProperty(memberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value);
                return;
            }

            throw new MissingMemberException(type.FullName, memberName);
        }
    }
}
