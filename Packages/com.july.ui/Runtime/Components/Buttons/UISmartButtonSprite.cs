using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>以同一交互状态切换正常和不可用图片，保留SmartButton输入与反馈规则。</summary>
    [AddComponentMenu("July/UI/Smart Button Sprite")]
    public sealed class UISmartButtonSprite : UISmartButton
    {
        [SerializeField] private Image _image;
        [SerializeField] private Sprite _normalSprite;
        [SerializeField] private Sprite _disabledSprite;

        private void OnEnable() => RefreshSprite();

        public override void SetInteractable(bool value)
        {
            base.SetInteractable(value);
            RefreshSprite();
        }

        private void RefreshSprite() => _image.sprite = IsInteractable ? _normalSprite : _disabledSprite;
    }
}
