using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Audio;

namespace July.UI
{
    /// <summary>
    /// 场景切换协调器。
    ///
    /// 用法：
    ///   await SceneTransitionHandler.EnterAsync();
    ///   // … 下载资源、切场景、初始化 …
    ///   await SceneTransitionHandler.ExitAsync();
    /// </summary>
    public static class SceneTransitionHandler
    {
        private static int _loadingWindowId;
        private static ISceneTransitionView _activeView;
        private static bool _initialized;

        public static void Initialize(int loadingWindowId, string loadingWindowName)
        {
            _loadingWindowId = loadingWindowId;
            _initialized = true;
        }

        public static async UniTask EnterAsync(object options = null, CancellationToken ct = default)
        {
            _activeView = null;

            var ctx = ArchContext.Current;
            var ui = ctx?.GetSystem<IUISystem>();

            if (_initialized && ui != null)
            {
                var view = await ui.OpenAsync(_loadingWindowId, ct: ct);
                _activeView = view as ISceneTransitionView;
            }

            if (_activeView != null)
                await _activeView.PlayEnterAsync(options);

            if (ui != null)
            {
                ui.CloseLayer(UILayer.Background, excludeWindowId: _loadingWindowId);
                ui.CloseLayer(UILayer.Normal, excludeWindowId: _loadingWindowId);
                ui.CloseLayer(UILayer.Popup, excludeWindowId: _loadingWindowId);
                ui.CloseLayer(UILayer.Top, excludeWindowId: _loadingWindowId);
            }

            ctx?.GetSystem<AudioSystem>()?.StopAllSfx();
        }

        public static async UniTask ExitAsync(CancellationToken ct = default)
        {
            if (_activeView != null)
            {
                await _activeView.PlayExitAsync();
                _activeView = null;
            }

            if (_initialized)
                await (ArchContext.Current?.GetSystem<IUISystem>()?.CloseAsync(_loadingWindowId, ct) ?? UniTask.CompletedTask);
        }
    }
}
