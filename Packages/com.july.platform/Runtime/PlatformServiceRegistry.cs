using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    /// <summary>
    /// Owns platform service registration and the common four-phase lifecycle.
    /// Services are initialized in registration order and shut down in reverse order.
    /// </summary>
    public sealed class PlatformServiceRegistry
    {
        private readonly Dictionary<Type, IPlatformService> _services = new();
        private readonly List<IPlatformService> _orderedServices = new();

        public PlatformServiceRegistry Register<T>(T service, bool replace = false)
            where T : class, IPlatformService
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            var contract = typeof(T);
            if (_services.TryGetValue(contract, out var previous))
            {
                if (!replace)
                    throw new InvalidOperationException(
                        $"Platform service is already registered: {contract.FullName}");

                _services[contract] = service;
                var index = _orderedServices.IndexOf(previous);
                if (index >= 0) _orderedServices[index] = service;
                return this;
            }

            _services.Add(contract, service);
            if (!_orderedServices.Contains(service))
                _orderedServices.Add(service);
            return this;
        }

        public T Get<T>() where T : class
            => Get(typeof(T)) as T;

        public object Get(Type contract)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            return _services.TryGetValue(contract, out var service) ? service : null;
        }

        internal async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            foreach (var service in _orderedServices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (service is INeedGetService accessor)
                    accessor.ServiceGetter = Get;
            }

            foreach (var service in _orderedServices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                service.Init();
            }

            foreach (var service in _orderedServices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                service.PostInit();
            }

            var tasks = new List<UniTask>(_orderedServices.Count);
            foreach (var service in _orderedServices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tasks.Add(service.PostInitAsync());
            }
            await UniTask.WhenAll(tasks);
        }

        internal void DeferAll()
        {
            foreach (var service in _orderedServices)
                service.DeferredInit();
        }

        internal void Shutdown()
        {
            for (var i = _orderedServices.Count - 1; i >= 0; i--)
            {
                var service = _orderedServices[i];
                service.Shutdown();
                if (service is INeedGetService accessor)
                    accessor.ServiceGetter = null;
            }

            _orderedServices.Clear();
            _services.Clear();
        }
    }
}
