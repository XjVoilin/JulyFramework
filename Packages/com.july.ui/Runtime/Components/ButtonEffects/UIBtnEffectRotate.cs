using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.UI
{
    /// <summary>
    /// 按钮子元素旋转效果。按下时旋转到目标角度，抬起时还原。
    /// </summary>
    public class UIBtnEffectRotate : UIBtnEffect
    {
        [Header("Rotate")]
        [FormerlySerializedAs("pressRotation")]
        [SerializeField] private Vector3 _pressRotation = new(0f, 0f, 15f);

        private Vector3 _originRotation;

        private void Awake()
        {
            _originRotation = transform.localEulerAngles;
        }

        protected override void OnPress()
        {
            tween?.Kill();
            tween = transform.DOLocalRotate(_originRotation + _pressRotation, duration)
                .SetDelay(delay)
                .SetEase(pressEase)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        protected override void OnRelease()
        {
            tween?.Kill();
            tween = transform.DOLocalRotate(_originRotation, duration)
                .SetEase(releaseEase)
                .SetUpdate(true)
                .SetLink(gameObject);
        }
    }
}
