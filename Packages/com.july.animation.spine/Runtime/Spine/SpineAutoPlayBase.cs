using Spine;
using Spine.Unity;
using UnityEngine;
using AnimationState = Spine.AnimationState;

namespace July.Animation.Spine
{
    /// <summary>
    /// Spine 自动播放基类：OnEnable 时按 intro → loop 顺序播放动画。
    /// 子类只需提供获取 AnimationState 的方式。
    /// </summary>
    public abstract class SpineAutoPlayBase : MonoBehaviour
    {
        [SpineAnimation] [SerializeField] protected string _introAnimation;
        [SpineAnimation] [SerializeField] protected string _loopAnimation;
        [SerializeField] protected bool _playOnEnable = true;
        [SerializeField] protected bool _hideOnComplete;

        protected abstract AnimationState GetAnimationState();

        protected virtual void OnEnable()
        {
            if (_playOnEnable)
                Play();
        }

        public void Play()
        {
            var animState = GetAnimationState();
            if (animState == null) return;

            if (!string.IsNullOrEmpty(_introAnimation))
            {
                var intro = animState.SetAnimation(0, _introAnimation, false);
                if (intro != null)
                {
                    if (!string.IsNullOrEmpty(_loopAnimation))
                        intro.Complete += OnIntroComplete;
                    else if (_hideOnComplete)
                        intro.Complete += OnPlayComplete;
                }
            }
            else if (!string.IsNullOrEmpty(_loopAnimation))
            {
                animState.SetAnimation(0, _loopAnimation, true);
            }
        }

        public void Stop()
        {
            var animState = GetAnimationState();
            if (animState != null)
                animState.ClearTracks();
        }

        private void OnIntroComplete(TrackEntry entry)
        {
            entry.Complete -= OnIntroComplete;
            var animState = GetAnimationState();
            if (animState != null && !string.IsNullOrEmpty(_loopAnimation))
                animState.SetAnimation(0, _loopAnimation, true);
        }

        private void OnPlayComplete(TrackEntry entry)
        {
            entry.Complete -= OnPlayComplete;
            if (_hideOnComplete)
                gameObject.SetActive(false);
        }
    }
}
