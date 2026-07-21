#if JULYGF_DEBUG
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace July.Diagnostics
{
    public sealed class GMOverlayRoot : MonoBehaviour
    {
        public static GMOverlayRoot Create(IReadOnlyList<GMCategoryInfo> categories)
        {
            var go = new GameObject("[GM Overlay]");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            var root = go.AddComponent<GMOverlayRoot>();
            var canvasTransform = go.transform;

            var safeArea = CreateSafeArea(canvasTransform);
            var blocker = CreateBlocker(canvasTransform);
            var panel = GMUGUIPanel.Create(canvasTransform, categories);
            panel.Blocker = blocker;

            GMFloatingBall.Create(safeArea, () => panel.Show());

            return root;
        }

        private static Transform CreateSafeArea(Transform parent)
        {
            var go = new GameObject("SafeArea", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.AddComponent<GMSafeAreaAdapter>();

            return go.transform;
        }

        private static GameObject CreateBlocker(Transform parent)
        {
            var go = new GameObject("Blocker", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;

            go.SetActive(false);
            return go;
        }
    }

    /// <summary>
    /// GM 专用简化版 SafeArea，不依赖 JulyCore。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    internal sealed class GMSafeAreaAdapter : MonoBehaviour
    {
        private RectTransform _rect;

        private void Awake() => _rect = GetComponent<RectTransform>();

        private void OnEnable()
        {
            var safeArea = Screen.safeArea;
            float sw = Screen.width;
            float sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            _rect.anchorMin = new Vector2(safeArea.x / sw, safeArea.y / sh);
            _rect.anchorMax = new Vector2(safeArea.xMax / sw, safeArea.yMax / sh);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
