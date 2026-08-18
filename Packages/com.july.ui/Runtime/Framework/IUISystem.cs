using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// UI 窗口模块的业务接口。窗口加载、资源所有权、层级、遮罩、动画、队列和生命周期状态均由实现隐藏。
    /// </summary>
    public interface IUISystem
    {
        Camera UICamera { get; }

        void SetMainProvider(IUIWindowProvider provider);
        void SetAdditionalProvider(IUIWindowProvider provider);
        void UnsetAdditionalProvider(IUIWindowProvider provider);

        void Open(int windowId, object data = null, CancellationToken ct = default);
        UniTask<UIView> OpenAsync(int windowId, object data = null, CancellationToken ct = default);

        void Close(int windowId);
        void Close(UIView view);
        UniTask CloseAsync(int windowId, CancellationToken ct = default);
        UniTask CloseAsync(UIView view, CancellationToken ct = default);
        void CloseLayer(UILayer layer, int excludeWindowId = -1);

        void ShowMask();
        void HideMask();

        void ShowTip(string message, float duration = 2f);
        void ConfigureTip(TipConfig config);
    }
}
