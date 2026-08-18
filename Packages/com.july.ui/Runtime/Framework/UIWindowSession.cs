using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.UI
{
    internal enum UIWindowLifecycle
    {
        Opening,
        Open,
        Closing,
        Closed,
    }

    /// <summary>
    /// 一个 windowId 从接受打开请求到关闭完成的完整生命周期。
    /// UISystem 只登记会话，不再分别维护「加载中」和「已打开」两套事实源。
    /// </summary>
    internal sealed class UIWindowSession
    {
        private readonly CancellationTokenSource _openingCancellation;
        private readonly UniTaskCompletionSource<UIView> _openedSignal = new();
        private readonly UniTaskCompletionSource _closedSignal = new();

        internal UIWindowSession(UIOpenOptions options, CancellationToken openingToken)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _openingCancellation = CancellationTokenSource.CreateLinkedTokenSource(openingToken);
        }

        internal int WindowId => Options.WindowIdentifier.ID;
        internal UIOpenOptions Options { get; }
        internal UIWindowLifecycle Lifecycle { get; private set; } = UIWindowLifecycle.Opening;
        internal bool WasOpened { get; private set; }
        internal UIView View { get; private set; }
        internal GameObject GameObject { get; private set; }
        internal CanvasGroup CanvasGroup { get; private set; }
        internal GameObject Mask { get; set; }
        internal CancellationToken OpeningToken => _openingCancellation.Token;

        internal UniTask<UIView> WaitUntilOpenedAsync(CancellationToken ct)
            => _openedSignal.Task.AttachExternalCancellation(ct);

        internal UniTask WaitUntilClosedAsync(CancellationToken ct)
            => _closedSignal.Task.AttachExternalCancellation(ct);

        internal bool TryAttachGameObject(GameObject gameObject)
        {
            if (Lifecycle == UIWindowLifecycle.Closed) return false;
            GameObject = gameObject;
            return true;
        }

        internal void SetView(UIView view, CanvasGroup canvasGroup)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            CanvasGroup = canvasGroup;
        }

        internal void MarkOpened()
        {
            if (Lifecycle != UIWindowLifecycle.Opening)
                throw new InvalidOperationException($"Window {WindowId} is no longer opening.");

            Lifecycle = UIWindowLifecycle.Open;
            WasOpened = true;
        }

        internal void CompleteOpening(UIView view) => _openedSignal.TrySetResult(view);

        internal void FailOpening(Exception exception)
        {
            if (exception is OperationCanceledException canceled)
                _openedSignal.TrySetCanceled(canceled.CancellationToken);
            else
                _openedSignal.TrySetException(exception);
        }

        /// <summary>
        /// 返回当前调用是否取得关闭权。wasOpen 用于区分取消 Opening 与关闭已展示窗口。
        /// </summary>
        internal bool TryBeginClosing(out bool wasOpen)
        {
            wasOpen = WasOpened;
            if (Lifecycle is UIWindowLifecycle.Closing or UIWindowLifecycle.Closed)
                return false;

            var cancelOpening = Lifecycle == UIWindowLifecycle.Opening;
            Lifecycle = UIWindowLifecycle.Closing;
            if (cancelOpening)
                _openingCancellation.Cancel();
            return true;
        }

        internal bool TryFinalize()
        {
            if (Lifecycle == UIWindowLifecycle.Closed) return false;
            Lifecycle = UIWindowLifecycle.Closed;
            return true;
        }

        internal void CompleteClosed()
        {
            _openingCancellation.Dispose();
            _closedSignal.TrySetResult();
        }
    }
}
