using DG.Tweening;
using UnityEngine;

namespace July.Animation
{
    /// <summary>
    /// 基于 DOTween 的简单动画基类，适用于 UI 和非 UI。
    /// OnEnable 自动播放，OnDisable 自动停止并还原状态。
    /// 子类只需实现 <see cref="OnCreateTween"/> 和 <see cref="OnReset"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class SimpleAnimationBase : MonoBehaviour, ISimpleAnimation
    {
        [Tooltip("忽略 timeScale，过场/暂停期间仍播放")]
        [SerializeField] private bool _ignoreTimeScale = true;

        private Tween _tween;

        public bool IsPlaying => _tween != null && _tween.IsPlaying();

        protected virtual void OnEnable() => Play();
        protected virtual void OnDisable() => Stop();

        public void Play()
        {
            Stop();
            _tween = OnCreateTween();
            if (_tween != null)
                _tween.SetUpdate(_ignoreTimeScale).SetLink(gameObject);
        }

        public void Stop()
        {
            _tween?.Kill();
            _tween = null;
            OnReset();
        }

        /// <summary>创建并返回动画 Tween/Sequence（含循环设置）。</summary>
        protected abstract Tween OnCreateTween();

        /// <summary>动画停止时还原到初始状态。</summary>
        protected abstract void OnReset();
    }
}
