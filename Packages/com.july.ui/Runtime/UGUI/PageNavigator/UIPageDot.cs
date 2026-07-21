using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 页码指示器圆点：通过 SerializeField 引用 Normal / Selected 两个子物体，
    /// 由 <see cref="UIPageNavigator"/> 批量管理。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPageDot : MonoBehaviour
    {
        [SerializeField] private GameObject _normal;
        [SerializeField] private GameObject _selected;

        public void SetSelected(bool isSelected)
        {
            if (_normal) _normal.SetActive(!isSelected);
            if (_selected) _selected.SetActive(isSelected);
        }
    }
}
