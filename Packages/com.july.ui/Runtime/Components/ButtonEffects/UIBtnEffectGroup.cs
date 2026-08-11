using UnityEngine;
using UnityEngine.EventSystems;

namespace July.UI
{
    /// <summary>
    /// 按钮子效果驱动器。挂在按钮根节点上，在按下/抬起时统一触发子效果。
    /// 与 UISmartButton 共存：UISmartButton 负责根节点缩放+点击事件，本组件负责子元素附加效果。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIBtnEffectGroup : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler
    {
        [SerializeField] private UIBtnEffect[] effects;

        private UISmartButton _smartButton;
        private bool _isPressed;

        private void Awake()
        {
            _smartButton = GetComponent<UISmartButton>();
        }

        private bool IsInteractable()
        {
            return _smartButton == null || _smartButton.IsInteractable;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable()) return;
            if (_isPressed) return;
            _isPressed = true;

            for (var i = 0; i < effects.Length; i++)
                effects[i].Press();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        private void Release()
        {
            if (!_isPressed) return;
            _isPressed = false;

            for (var i = 0; i < effects.Length; i++)
                effects[i].Release();
        }
    }
}
