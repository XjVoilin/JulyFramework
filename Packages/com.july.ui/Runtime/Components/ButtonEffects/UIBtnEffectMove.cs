using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.UI
{
    /// <summary>
    /// 按钮子元素位移效果。按下时偏移指定量，抬起时还原。
    /// </summary>
    public class UIBtnEffectMove : UIBtnEffect
    {
        [Header("Move")]
        [FormerlySerializedAs("pressOffset")]
        [SerializeField] private Vector3 _pressOffset;

        private Vector3 _originPos;

        private void Awake()
        {
            _originPos = transform.localPosition;
        }

        protected override void OnPress()
        {
            tween?.Kill();
            tween = transform.DOLocalMove(_originPos + _pressOffset, duration)
                .SetDelay(delay)
                .SetEase(pressEase)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        protected override void OnRelease()
        {
            tween?.Kill();
            tween = transform.DOLocalMove(_originPos, duration)
                .SetEase(releaseEase)
                .SetUpdate(true)
                .SetLink(gameObject);
        }
    }
}
