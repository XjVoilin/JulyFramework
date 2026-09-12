using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;

namespace July.Bootstrap
{
    internal sealed class InitAppSystemsStep : ILaunchStep
    {
        private readonly AppRegistration _state;
        public string Name => "Init All Systems";
        internal InitAppSystemsStep(AppRegistration state) => _state = state;
        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await _state.Registrar.PreInitializeAsync(ct);
            ct.ThrowIfCancellationRequested();
            await July.Arch.ArchContext.Current.InitializeAsync(ct);
            ct.ThrowIfCancellationRequested();
            return true;
        }
    }
}