using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.UI
{
    internal interface IUIWindowOpener
    {
        UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct);
    }

    /// <summary>
    /// 串行窗口调度器。它只决定请求何时获得打开资格，窗口实例和生命周期仍由 UISystem 管理。
    /// 每个请求返回的任务都会在该请求实际打开、失败或取消时完成。
    /// </summary>
    internal sealed class UIWindowSequencer
    {
        private static readonly CancellationToken CanceledToken = new(true);

        private readonly IUIWindowOpener _opener;
        private readonly LinkedList<SeqRequest> _queue = new();
        private const int Invalid = -1;

        private int _activeWindowId = Invalid;
        private bool _advancing;
        private bool _shutdown;

        private sealed class SeqRequest
        {
            internal UIOpenOptions Options;
            internal CancellationToken CancellationToken;
            internal readonly UniTaskCompletionSource<UIView> Completion = new();
        }

        internal UIWindowSequencer(IUIWindowOpener opener)
        {
            _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        }

        internal UniTask<UIView> RequestAsync(UIOpenOptions options, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled<UIView>(ct);
            if (_shutdown)
                return UniTask.FromCanceled<UIView>(CanceledToken);

            var request = new SeqRequest
            {
                Options = options,
                CancellationToken = ct,
            };

            if (options.QueueMode == UIQueueMode.EnqueueFirst)
                _queue.AddFirst(request);
            else
                _queue.AddLast(request);

            EnsureAdvancing();
            return request.Completion.Task.AttachExternalCancellation(ct);
        }

        internal void OnWindowClosed(int windowId)
        {
            if (windowId != _activeWindowId) return;
            _activeWindowId = Invalid;
            EnsureAdvancing();
        }

        internal void Clear() => CancelPending(_ => true);

        internal void ClearLayer(UILayer layer, int excludeWindowId = Invalid)
        {
            CancelPending(options => options.Layer == layer
                                     && options.WindowIdentifier.ID != excludeWindowId);
        }

        internal void Shutdown()
        {
            _shutdown = true;
            Clear();
            _activeWindowId = Invalid;
        }

        private void EnsureAdvancing()
        {
            if (_shutdown || _advancing || _activeWindowId != Invalid || _queue.Count == 0)
                return;
            AdvanceAsync().Forget();
        }

        private async UniTask AdvanceAsync()
        {
            _advancing = true;
            try
            {
                while (!_shutdown && _activeWindowId == Invalid && _queue.Count > 0)
                {
                    var request = _queue.First.Value;
                    _queue.RemoveFirst();

                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        request.Completion.TrySetCanceled(request.CancellationToken);
                        continue;
                    }

                    _activeWindowId = request.Options.WindowIdentifier.ID;
                    try
                    {
                        var view = await _opener.OpenCoreAsync(request.Options, request.CancellationToken);
                        if (view == null)
                            _activeWindowId = Invalid;
                        request.Completion.TrySetResult(view);
                        if (view != null)
                            return;
                    }
                    catch (OperationCanceledException canceled)
                    {
                        _activeWindowId = Invalid;
                        request.Completion.TrySetCanceled(canceled.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _activeWindowId = Invalid;
                        request.Completion.TrySetException(ex);
                    }
                }
            }
            finally
            {
                _advancing = false;
                EnsureAdvancing();
            }
        }

        private void CancelPending(Func<UIOpenOptions, bool> predicate)
        {
            var node = _queue.First;
            while (node != null)
            {
                var next = node.Next;
                if (predicate(node.Value.Options))
                {
                    _queue.Remove(node);
                    node.Value.Completion.TrySetCanceled(CanceledToken);
                }
                node = next;
            }
        }
    }
}
