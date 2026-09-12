using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;

namespace July.Bootstrap
{
    internal sealed class RegisterAppSystemsStep : ILaunchStep
    {
        private readonly HotUpdateConfig _options;
        private readonly AppRegistration _state;
        public string Name => "Register HotUpdate Systems";
        internal RegisterAppSystemsStep(HotUpdateConfig options, AppRegistration state) { _options = options; _state = state; }
        public UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != _options.RegistrarAssembly) continue;
                type = assembly.GetType(_options.RegistrarType, throwOnError: true);
                break;
            }
            if (type == null) throw new InvalidOperationException($"Registrar assembly is not loaded: {_options.RegistrarAssembly}");
            if (type.IsAbstract || !typeof(IHotUpdateRegistrar).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException($"Registrar must implement IHotUpdateRegistrar and have a public parameterless constructor: {type.FullName}");
            var registrar = (IHotUpdateRegistrar)Activator.CreateInstance(type);
            registrar.Register();
            _state.Registrar = registrar;
            return UniTask.FromResult(true);
        }
    }
}