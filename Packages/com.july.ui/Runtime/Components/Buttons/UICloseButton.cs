using July.Arch;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 挂载到 CloseBtn 上，自动查找父级 UIView 并在点击时关闭窗口。
    /// 无需每个窗口重复编写关闭逻辑。
    /// </summary>
    [RequireComponent(typeof(UISmartButton))]
    public class UICloseButton : ArchBehaviour
    {
        private UIView _window;
        private UISmartButton _button;

        private void Awake()
        {
            _button = GetComponent<UISmartButton>();
            _window = GetComponentInParent<UIView>();
            if (_window == null)
                Debug.LogWarning($"[UICloseButton] 未找到父级 UIView: {gameObject.name}");
            _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            if (_window != null && _window.IsOpened)
                this.GetSystem<IUISystem>()?.Close(_window);
        }
    }
}
