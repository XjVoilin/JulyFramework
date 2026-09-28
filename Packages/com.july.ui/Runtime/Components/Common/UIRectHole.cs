using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 在本 Graphic 内绘制矩形镂空遮罩。窗口提供本地坐标洞口，不查询业务目标。
    /// raycastTarget 控制是否接收射线，passThroughHole 控制是否放行洞口。
    /// </summary>
    [AddComponentMenu("July UI/Rect Hole")]
    public sealed class UIRectHole : MaskableGraphic, ICanvasRaycastFilter
    {
        [SerializeField] private bool holeEnabled;
        [SerializeField] private Rect hole;
        [SerializeField] private bool passThroughHole = true;

        public void SetHole(Rect localRect)
        {
            hole = localRect;
            holeEnabled = true;
            SetVerticesDirty();
        }

        public void ClearHole()
        {
            holeEnabled = false;
            SetVerticesDirty();
        }

        public bool PassThroughHole
        {
            get => passThroughHole;
            set => passThroughHole = value;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var bounds = rectTransform.rect;
            if (!holeEnabled || hole.width <= 0f || hole.height <= 0f)
            {
                AddRect(vh, bounds);
                return;
            }
            var minX = Mathf.Clamp(hole.xMin, bounds.xMin, bounds.xMax);
            var maxX = Mathf.Clamp(hole.xMax, bounds.xMin, bounds.xMax);
            var minY = Mathf.Clamp(hole.yMin, bounds.yMin, bounds.yMax);
            var maxY = Mathf.Clamp(hole.yMax, bounds.yMin, bounds.yMax);
            AddRect(vh, Rect.MinMaxRect(bounds.xMin, bounds.yMin, minX, bounds.yMax));
            AddRect(vh, Rect.MinMaxRect(maxX, bounds.yMin, bounds.xMax, bounds.yMax));
            AddRect(vh, Rect.MinMaxRect(minX, bounds.yMin, maxX, minY));
            AddRect(vh, Rect.MinMaxRect(minX, maxY, maxX, bounds.yMax));
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!holeEnabled || !passThroughHole) return true;
            return !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, screenPoint, eventCamera, out var localPoint) || !hole.Contains(localPoint);
        }

        private void AddRect(VertexHelper vh, Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            var start = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.up);
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.one);
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.right);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
