using System;
using UnityEngine;

namespace July.Arch
{
    /// <summary>
    /// 场景对象 / UI 子组件基类。
    /// <para>
    /// 独占 Unity 的 Awake / OnDestroy / OnEnable / OnDisable，
    /// 子类使用 OnViewAwake / OnViewDestroy / OnViewEnable / OnViewDisable 钩子。
    /// </para>
    /// <para>
    /// 实现 <see cref="ISingletonView"/> 的子类在 Awake 时自动注册到 ArchContext。
    /// </para>
    /// </summary>
    public abstract class GameView : ArchBehaviour
    {
        #region 能力方法 — 允许子类省略 this. 前缀

        protected T GetStore<T>() where T : StoreBase
            => ArchContext.Current?.GetStore<T>();

        protected T GetView<T>() where T : GameView
            => ArchContext.Current?.GetView<T>();

        protected void Subscribe<T>(Action<T> handler)
            => ArchContext.Current?.Event?.Subscribe(handler, this);

        protected void Unsubscribe<T>(Action<T> handler)
            => ArchContext.Current?.Event?.Unsubscribe(handler);

        protected void Publish<T>(T eventData)
            => ArchContext.Current?.Event?.Publish(eventData);

        protected T GetSystem<T>() where T : class
            => ArchContext.Current?.GetSystem<T>();

        #endregion

        #region Lifecycle — 框架独占，子类使用 OnViewXxx 钩子

        private void Awake()
        {
            if (this is ISingletonView)
                ArchContext.Current?.RegisterView(this);
            OnViewAwake();
        }

        private void OnDestroy()
        {
            OnViewDestroy();
            if (this is ISingletonView)
                ArchContext.Current?.UnregisterView(this);
        }

        private void OnEnable() => OnViewEnable();

        private void OnDisable()
        {
            this.UnsubscribeAll();
            OnViewDisable();
        }

        #endregion

        #region 子类钩子

        protected virtual void OnViewAwake() { }
        protected virtual void OnViewDestroy() { }
        protected virtual void OnViewEnable() { }
        protected virtual void OnViewDisable() { }

        #endregion
    }
}
