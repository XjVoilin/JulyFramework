using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 通用物品格子组件。
    /// 负责纯视觉展示（图标、数量角标、选中框、空/满两态），不持有业务逻辑。
    /// 由父级 View 通过公开 API 驱动状态。
    /// </summary>
    public class UIItemSlot : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _quantityText;
        [SerializeField] private GameObject _selectedFrame;
        [SerializeField] private GameObject _emptyRoot;
        [SerializeField] private GameObject _filledRoot;

        public int Index { get; private set; }
        public bool IsEmpty { get; private set; } = true;

        public event Action<UIItemSlot> OnClicked;

        public void SetIndex(int index) => Index = index;

        public void SetItem(Sprite icon, int quantity, Color? iconTint = null)
        {
            IsEmpty = false;
            if (_filledRoot) _filledRoot.SetActive(true);
            if (_emptyRoot) _emptyRoot.SetActive(false);

            if (_icon)
            {
                _icon.sprite = icon;
                _icon.color = iconTint ?? Color.white;
                _icon.enabled = true;
            }

            if (_quantityText)
            {
                _quantityText.text = quantity > 1 ? quantity.ToString() : "";
                _quantityText.gameObject.SetActive(quantity > 1);
            }
        }

        public void SetEmpty()
        {
            IsEmpty = true;
            if (_filledRoot) _filledRoot.SetActive(false);
            if (_emptyRoot) _emptyRoot.SetActive(true);
            if (_selectedFrame) _selectedFrame.SetActive(false);
        }

        public void SetSelected(bool selected)
        {
            if (_selectedFrame) _selectedFrame.SetActive(selected);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            OnClicked?.Invoke(this);
        }
    }
}
