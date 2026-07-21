using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 美术字数字显示：通过 Sprite 数组（0-9）拼接显示整数。
    /// 轻量实现，不依赖 LayoutGroup，不动态创建对象。
    /// 数字 slot 和装饰图（前缀/后缀均可）的位置全由美术在 prefab 中自由摆放。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIArtNumber : MonoBehaviour
    {
        [SerializeField] private Sprite[] _digits = new Sprite[10];
        [SerializeField] private Image[] _digitSlots;
        [SerializeField] private bool _centerAlign;
        [SerializeField] private float _spacing;

        private int _currentNumber = -1;

        public int CurrentNumber => _currentNumber;

        public void SetNumber(int number)
        {
            if (number == _currentNumber) return;
            _currentNumber = number;
            Refresh();
        }

        private void Refresh()
        {
            if (_digitSlots == null || _digitSlots.Length == 0) return;

            int number = Mathf.Max(0, _currentNumber);
            string numStr = number.ToString();
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
                    if (_centerAlign)
                        totalWidth += _digitSlots[i].rectTransform.sizeDelta.x;
                }
                else
                {
                    _digitSlots[i].gameObject.SetActive(false);
                }
            }

            if (_centerAlign && digitCount > 0)
            {
                totalWidth += (digitCount - 1) * _spacing;
                float x = -totalWidth * 0.5f;
                for (int i = 0; i < digitCount; i++)
                {
                    if (_digitSlots[i] == null) continue;
                    float w = _digitSlots[i].rectTransform.sizeDelta.x;
                    _digitSlots[i].rectTransform.anchoredPosition = new Vector2(x + w * 0.5f, 0f);
                    x += w + _spacing;
                }
            }
        }
    }
}
