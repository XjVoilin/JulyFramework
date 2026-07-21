using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.UI
{
    /// <summary>
    /// 窗口打开能力 —— 由 <see cref="UISystem"/> 实现，供 <see cref="UIWindowSequencer"/> 调用；
    /// 抽象为接口以解耦 Sequencer 与 UISystem 具体类，便于测试注入 fake。
    /// </summary>
    internal interface IUIWindowOpener
    {
        UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct);
    }

    /// <summary>
    /// UI 窗口串行调度器 —— 全局单队列，保证同一时刻最多只有一个
    /// <see cref="UIQueueMode"/>.Enqueue / EnqueueFirst 的窗口在展示。
    /// 由 <see cref="UISystem"/> 持有，监听 <see cref="UICloseEvent"/> 驱动链式推进。
    /// <para>机制只看 <see cref="UIQueueMode"/>，不区分 <c>windowId</c> 异同：升级奖励、成就、
    /// 领奖等不同窗口交错触发时，按入队顺序串行展示，一次只开一个。</para>
    /// <para>语义契约：队列推进只由「当前活跃串行窗口自身的关闭事件」触发；
    /// 该窗口存活期间打开的子窗口（不同 windowId 或 QueueMode=None）的开关不影响本队列。</para>
    /// </summary>
    internal sealed class UIWindowSequencer
    {
        private readonly IUIWindowOpener _opener;
        private readonly LinkedList<SeqRequest> _queue = new();
        private const int Invalid = -1;
        private int _activeWindowId = Invalid;

        private struct SeqRequest
        {
            public UIOpenOptions Options;
            public CancellationToken Ct;
        }

        internal UIWindowSequencer(IUIWindowOpener opener)
        {
            _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        }

        /// <summary>
        /// 串行打开请求。无活跃串行窗口时立即打开并记录 <c>_activeWindowId</c>；
        /// 已有活跃串行窗口时入队（EnqueueFirst 入队首，否则入队尾），返回 null。
        /// </summary>
        internal async UniTask<UIView> RequestAsync(UIOpenOptions options, CancellationToken ct)
        {
            if (_activeWindowId != Invalid)
            {
                var req = new SeqRequest { Options = options, Ct = ct };
                if (options.QueueMode == UIQueueMode.EnqueueFirst)
                    _queue.AddFirst(req);
                else
                    _queue.AddLast(req);
                return null;
            }

            return await ActivateAsync(options, ct);
        }

        /// <summary>
        /// 窗口关闭事件回调。仅当关闭的是当前活跃串行窗口时推进队列；
        /// 子窗口 / None 窗口关闭因 windowId 不匹配被忽略。
        /// </summary>
        internal void OnWindowClosed(int windowId)
        {
            if (windowId != _activeWindowId) return;
            _activeWindowId = Invalid;

            if (_queue.Count == 0) return;
            ActivateNextAsync().Forget();
        }

        /// <summary>清空等待队列。不影响当前已打开的活跃串行窗口。</summary>
        internal void Clear() => _queue.Clear();

        internal void Shutdown()
        {
            _queue.Clear();
            _activeWindowId = Invalid;
        }

        private async UniTask<UIView> ActivateAsync(UIOpenOptions options, CancellationToken ct)
        {
            _activeWindowId = options.WindowIdentifier.ID;
            var view = await _opener.OpenCoreAsync(options, ct);
            if (view == null)
            {
                // 打开失败（prefab 缺失等）：回滚 _activeWindowId 并尝试下一个，避免队列卡死
                _activeWindowId = Invalid;
                ActivateNextAsync().Forget();
            }
            return view;
        }

        private async UniTask ActivateNextAsync()
        {
            if (_queue.Count == 0) return;
            var next = _queue.First.Value;
            _queue.RemoveFirst();
            await ActivateAsync(next.Options, next.Ct);
        }
    }
}
