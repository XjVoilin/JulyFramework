using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    public sealed class PlatformCapabilityRegistry
    {
        private readonly Dictionary<Type, IPlatformCapability> _capabilities = new();

        public PlatformCapabilityRegistry Register<T>(T capability, bool replace = false)
            where T : class, IPlatformCapability
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            var contract = typeof(T);
            if (!replace && _capabilities.ContainsKey(contract))
                throw new InvalidOperationException($"平台能力已注册: {contract.FullName}");
            _capabilities[contract] = capability;
            return this;
        }

        public T Get<T>() where T : class =>
            _capabilities.TryGetValue(typeof(T), out var capability) ? capability as T : null;

        internal async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            foreach (var capability in _capabilities.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await capability.InitializeAsync(cancellationToken);
            }
        }

        internal void DeferAll()
        {
            foreach (var capability in _capabilities.Values)
                if (capability is IDeferredPlatformCapability deferred) deferred.Defer();
        }

        internal void Shutdown()
        {
            foreach (var capability in _capabilities.Values) capability.Shutdown();
            _capabilities.Clear();
        }
    }
}
