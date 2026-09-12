using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;

namespace July.Bootstrap
{
    internal sealed class LaunchGameStep : ILaunchStep
    {
        private readonly AppRegistration _state;
        private readonly IBootstrapView _view;
        public string Name => "Launch Game";
        internal LaunchGameStep(AppRegistration state, IBootstrapView view) { _state = state; _view = view; }
        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _view.PrepareForGame();
            await _state.Registrar.OnGameLaunch(ct);
            ct.ThrowIfCancellationRequested();
            return true;
        }
    }
}