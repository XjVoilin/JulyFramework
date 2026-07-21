using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.Analytics
{
    /// <summary>把稳定的 IAnalyticsSystem API 分发给一个或多个项目注入的 SDK 通道。</summary>
    public sealed class AnalyticsSystem : SystemBase, IAnalyticsSystem
    {
        private readonly List<IAnalyticsChannel> _channels = new();
        private bool _isInitialized;

        public bool IsEnabled { get; private set; } = true;

        public AnalyticsSystem(params IAnalyticsChannel[] channels)
        {
            if (channels == null) return;
            foreach (var channel in channels) RegisterChannel(channel);
        }

        public void RegisterChannel(IAnalyticsChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (_channels.Contains(channel)) return;
            _channels.Add(channel);
            if (_isInitialized) InvokeSafely(channel, value => value.Initialize());
        }

        public bool UnregisterChannel(IAnalyticsChannel channel, bool shutdown = true)
        {
            if (channel == null || !_channels.Remove(channel)) return false;
            if (_isInitialized && shutdown) InvokeSafely(channel, value => value.Shutdown());
            return true;
        }

        protected override UniTask OnInitializeAsync()
        {
            foreach (var channel in _channels)
                InvokeSafely(channel, value => value.Initialize());
            _isInitialized = true;
            return UniTask.CompletedTask;
        }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;

        public void Track(string eventName, Dictionary<string, object> parameters = null)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(eventName)) return;
            ForEach(channel => channel.Track(eventName, parameters));
        }

        public void Track<T>(T evt) where T : struct, IBIEvent =>
            Track(evt.EventName, evt.ToParams());

        public void SetUserId(string userId) => ForEach(channel => channel.SetUserId(userId));

        public void SetUserProperties(Dictionary<string, object> properties)
        {
            if (properties != null) ForEach(channel => channel.SetUserProperties(properties));
        }

        public void SetUserProperties<T>(T props) where T : struct, IBIProperties =>
            SetUserProperties(props.ToParams());

        public void Flush() => ForEach(channel => channel.Flush());
        public void SetLogEnabled(bool enabled) =>
            ForEach(channel => channel.SetLogEnabled(enabled));

        protected override void OnShutdown()
        {
            foreach (var channel in _channels)
                InvokeSafely(channel, value => value.Shutdown());
            _isInitialized = false;
        }

        private void ForEach(Action<IAnalyticsChannel> action)
        {
            foreach (var channel in _channels) InvokeSafely(channel, action);
        }

        private static void InvokeSafely(IAnalyticsChannel channel,
            Action<IAnalyticsChannel> action)
        {
            try { action(channel); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
