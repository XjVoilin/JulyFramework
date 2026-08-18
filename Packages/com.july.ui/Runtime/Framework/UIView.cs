using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// UI 面板基类 — 继承 ArchBehaviour，由 UISystem 驱动生命周期。
    /// <para>与 <see cref="GameView"/> 平级，不经过 GameView 继承链。</para>
    /// <para>不占用 Unity 生命周期方法；事件清理由内部生命周期统一保证。</para>
    /// </summary>
    public abstract class UIView : ArchBehaviour
    {
        private object _data;
        private bool _prepared;

        public bool IsOpened { get; private set; }
        public int WindowId { get; internal set; }

        #region Internal — UISystem 驱动

        internal void InternalSetData(object data) => _data = data;

        internal void InternalBeforeOpen()
        {
            _prepared = true;
            OnBeforeOpen();
        }

        internal void InternalOpen()
        {
            IsOpened = true;
            OnOpen();
        }

        internal void InternalClose()
        {
            if (!IsOpened) return;
            IsOpened = false;
            this.UnsubscribeAll();
            OnClose();
        }

        internal void InternalAfterClose()
        {
            if (!_prepared) return;
            _prepared = false;
            this.UnsubscribeAll();
            OnAfterClose();
        }

        #endregion

        #region 子类 UI 生命周期钩子

        /// <summary>数据已注入，设置 UI 初始状态。在 open 动画之前调用。</summary>
        protected virtual void OnBeforeOpen() { }

        /// <summary>Open 动画结束，面板可交互。订阅事件推荐放这里。</summary>
        protected virtual void OnOpen() { }

        /// <summary>关闭开始，业务清理。</summary>
        protected virtual void OnClose() { }

        /// <summary>Close 动画结束，即将销毁。</summary>
        protected virtual void OnAfterClose() { }

        #endregion

        #region 工具方法

        protected T GetData<T>()
        {
            if (_data is T d) return d;
            return default;
        }

        protected void CloseWindow()
        {
            this.GetSystem<IUISystem>()?.Close(this);
        }

        protected UniTask CloseWindowAsync(CancellationToken ct = default)
        {
            return this.GetSystem<IUISystem>()?.CloseAsync(this, ct) ?? UniTask.CompletedTask;
        }

        #endregion
    }
}
