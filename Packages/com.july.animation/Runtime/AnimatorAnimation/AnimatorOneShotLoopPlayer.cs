using System;
using UnityEngine;

namespace July.Animation
{
    /// <summary>
    /// 播放一个 Animator 状态一次，完成后切换到循环状态。
    /// 循环状态使用的 AnimationClip 需要启用 Loop Time。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class AnimatorOneShotLoopPlayer : MonoBehaviour
    {
        [Tooltip("先播放一次的状态名。")]
        [SerializeField] private string _oneShotStateName;

        [Tooltip("随后循环播放的状态名，对应的 AnimationClip 需要启用 Loop Time。")]
        [SerializeField] private string _loopStateName;

        [Tooltip("状态所在的 Animator Layer 索引。")]
        [SerializeField, Min(0)] private int _layerIndex;

        [Tooltip("切换到循环状态时的融合时长（秒）。设为 0 表示立即切换。")]
        [SerializeField, Min(0f)] private float _transitionDuration = 0.15f;

        [Tooltip("OnEnable 时自动播放。")]
        [SerializeField] private bool _playOnEnable = true;

        private Animator _animator;
        private bool _waitingForOneShot;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (_playOnEnable)
                Play();
        }

        private void Update()
        {
            if (!_waitingForOneShot)
                return;

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(_layerIndex);
            if (stateInfo.normalizedTime < 1f)
                return;

            if (_transitionDuration == 0f)
                _animator.Play(_loopStateName, _layerIndex, 0f);
            else
                _animator.CrossFadeInFixedTime(
                    _loopStateName,
                    _transitionDuration,
                    _layerIndex,
                    0f);
            _waitingForOneShot = false;
        }

        /// <summary>从头播放一次指定状态，完成后切换到循环状态。</summary>
        public void Play()
        {
            Play(_oneShotStateName);
        }

        /// <summary>Plays the requested state once, then switches to the configured loop state.</summary>
        public void Play(string oneShotStateName)
        {
            if (string.IsNullOrWhiteSpace(oneShotStateName))
                throw new ArgumentException("One-shot state name cannot be empty.", nameof(oneShotStateName));

            _animator.Play(oneShotStateName, _layerIndex, 0f);
            _animator.Update(0f);
            _waitingForOneShot = true;
        }
    }
}
