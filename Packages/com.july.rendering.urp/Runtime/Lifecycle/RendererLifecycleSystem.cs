using Cysharp.Threading.Tasks;
using July.Arch;
using July.Scene;

namespace July.Rendering.URP.Lifecycle
{
    public sealed class RendererLifecycleSystem : SystemBase
    {
        private RendererLifecycleConfig _config;
        private IdleRendererReclaimer _reclaimer;

        public void Configure(RendererLifecycleConfig config) => _config = config;

        protected override UniTask OnInitializeAsync()
        {
            _reclaimer = new IdleRendererReclaimer(_config);
            Subscribe<SceneLoadCompleteEvent>(_ => _reclaimer.RequestReclaim());
            Subscribe<SceneLoadFailedEvent>(_ => _reclaimer.RequestReclaim());
            Subscribe<SceneUnloadCompleteEvent>(_ => _reclaimer.RequestReclaim());
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown()
        {
            _reclaimer.Dispose();
            _reclaimer = null;
        }
    }
}
