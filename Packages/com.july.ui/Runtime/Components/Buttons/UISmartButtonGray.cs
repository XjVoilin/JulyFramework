using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 继承 <see cref="UISmartButton"/>，SetInteractable 时自动联动灰度视觉。
    /// 需要同 GameObject 上挂 <see cref="UIGrayGroup"/>。
    /// </summary>
    [RequireComponent(typeof(UIGrayGroup))]
    public class UISmartButtonGray : UISmartButton
    {
        private UIGrayGroup _grayGroup;

        public override void SetInteractable(bool value)
        {
            base.SetInteractable(value);
            if (!_grayGroup) _grayGroup = GetComponent<UIGrayGroup>();
            _grayGroup.SetGray(!value);
        }
    }
}
