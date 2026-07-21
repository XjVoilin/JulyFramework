using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 按钮子元素效果基类。挂在按钮的子节点上，由 UIBtnEffectGroup 统一驱动。
    /// </summary>
    public abstract class UIBtnEffect : MonoBehaviour
    {
        [SerializeField] protected float delay;
        [SerializeField] protected float duration = 0.15f;
        [SerializeField] protected Ease pressEase = Ease.OutBack;
        [SerializeField] protected Ease releaseEase = Ease.InBack;

        protected Tween tween;

        protected virtual void OnDestroy()
        {
            tween?.Kill();
        }

        public void Press()
        {
            if (!enabled) return;
            OnPress();
        }

        public void Release()
        {
            if (!enabled) return;
            OnRelease();
        }

        protected abstract void OnPress();
        protected abstract void OnRelease();
    }
}
