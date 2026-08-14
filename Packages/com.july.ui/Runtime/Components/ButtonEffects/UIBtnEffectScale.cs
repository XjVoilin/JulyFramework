using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.UI
{
    /// <summary>
    /// 按钮子元素缩放效果。按下时缩放到目标倍率，抬起时还原。
    /// </summary>
    public class UIBtnEffectScale : UIBtnEffect
    {
        [Header("Scale")]
        [FormerlySerializedAs("scaleMultiplier")]
        [SerializeField] private float _scaleMultiplier = 1.1f;

        private Vector3 _originScale;
        private Vector3 _pressedScale;

        private void Awake()
        {
            _originScale = transform.localScale;
            _pressedScale = _originScale * _scaleMultiplier;
        }

        protected override void OnPress()
        {
            tween?.Kill();
            tween = transform.DOScale(_pressedScale, duration)
                .SetDelay(delay)
                .SetEase(pressEase)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        protected override void OnRelease()
        {
            tween?.Kill();
            tween = transform.DOScale(_originScale, duration)
                .SetEase(releaseEase)
                .SetUpdate(true)
                .SetLink(gameObject);
        }
    }
}
