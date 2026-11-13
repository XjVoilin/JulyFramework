using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    public enum UIArtNumberLayout
    {
        Manual,
        Left,
        Center,
        Right
    }

    /// <summary>
    /// 美术字数字显示：通过 Sprite 数组（0-9）拼接显示整数。
    /// 轻量实现，不依赖 LayoutGroup，不动态创建对象。
    /// 手动模式保留数位位置；自动模式按组件矩形的左边、中线或右边排列，装饰图独立摆放。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIArtNumber : MonoBehaviour
    {
        [SerializeField] private Sprite[] _digits = new Sprite[10];
        [SerializeField] private Image[] _digitSlots;
        [SerializeField, Min(1)] private int _minimumDigits = 1;
        [SerializeField, Tooltip("Manual preserves slot positions. Left, Center and Right align within this component's RectTransform.")] private UIArtNumberLayout _layout;
        [SerializeField, Tooltip("Spacing between digit slots in Left, Center or Right layouts.")] private float _spacing;

        private int _currentNumber = -1;

        public int CurrentNumber => _currentNumber;

        public void SetNumber(int number)
        {
            if (number == _currentNumber) return;
            _currentNumber = number;
            Refresh(number);
        }

#if UNITY_EDITOR
        // Editor preview must not replace the runtime value or its change cache.
        internal void RefreshEditorPreview(int number) => Refresh(number);
#endif

        private void Refresh(int number)
        {
            if (_digitSlots == null || _digitSlots.Length == 0) return;

            number = Mathf.Max(0, number);
            string numStr = number.ToString().PadLeft(_minimumDigits, '0');
            int digitCount = Mathf.Min(numStr.Length, _digitSlots.Length);

            float totalWidth = 0f;
            for (int i = 0; i < _digitSlots.Length; i++)
            {
                if (_digitSlots[i] == null) continue;

                if (i < digitCount)
                {
                    _digitSlots[i].gameObject.SetActive(true);
                    int d = numStr[i] - '0';
                    _digitSlots[i].sprite = _digits[d];
                    _digitSlots[i].SetNativeSize();
                    if (_layout != UIArtNumberLayout.Manual)
                        totalWidth += _digitSlots[i].rectTransform.sizeDelta.x;
                }
                else
                {
                    _digitSlots[i].gameObject.SetActive(false);
                }
            }

            if (_layout != UIArtNumberLayout.Manual && digitCount > 0)
            {
                totalWidth += (digitCount - 1) * _spacing;
                var bounds = ((RectTransform)transform).rect;
                float x = _layout switch
                {
                    UIArtNumberLayout.Left => bounds.xMin,
                    UIArtNumberLayout.Center => bounds.center.x - totalWidth * 0.5f,
                    UIArtNumberLayout.Right => bounds.xMax - totalWidth,
                    _ => throw new System.ArgumentOutOfRangeException(nameof(_layout), _layout, null)
                };
                for (int i = 0; i < digitCount; i++)
                {
                    if (_digitSlots[i] == null) continue;
                    var slot = _digitSlots[i].rectTransform;
                    float w = slot.sizeDelta.x;
                    // Position in the owner rectangle, independently of each slot's anchors.
                    slot.position = transform.TransformPoint(new Vector3(x + w * slot.pivot.x, bounds.center.y, 0f));
                    x += w + _spacing;
                }
            }
        }
    }
}
