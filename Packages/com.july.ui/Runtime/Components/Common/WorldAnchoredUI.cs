using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 挂在 UI 上，将位置对齐到一个 2D 世界节点。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WorldAnchoredUI : MonoBehaviour
    {
        [SerializeField] private Transform _worldAnchor;

        private RectTransform _rect;
        private RectTransform _parentRect;
        private Camera _cam;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _parentRect = _rect.parent as RectTransform;
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            _cam = canvas.worldCamera;
        }

        private void Start() => Recalculate();

        public void Recalculate()
        {
            if (_worldAnchor == null || _cam == null || _parentRect == null) return;

            var screenPos = _cam.WorldToScreenPoint(_worldAnchor.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect, screenPos, _cam, out var localPoint);
            _rect.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
        }
    }
}
