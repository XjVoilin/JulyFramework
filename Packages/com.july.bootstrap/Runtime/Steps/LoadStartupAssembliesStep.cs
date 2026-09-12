using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Launch;
using July.Resource;

namespace July.Bootstrap
{
    internal sealed class LoadStartupAssembliesStep : ILaunchStep
    {
        private readonly IReadOnlyList<string> _aot;
        private readonly string _registrarAssembly;
        public string Name => "Load Startup Assemblies";
        internal LoadStartupAssembliesStep(IReadOnlyList<string> aot, string registrarAssembly)
        { _aot = aot; _registrarAssembly = registrarAssembly; }
        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            await BootstrapAssemblyLoader.LoadAsync(ArchContext.Current.GetSystem<IResourceSystem>(), _aot, _registrarAssembly, ct);
            ct.ThrowIfCancellationRequested();
            return true;
        }
    }
}