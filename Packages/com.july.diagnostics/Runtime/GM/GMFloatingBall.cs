#if JULYGF_DEBUG
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.Diagnostics
{
    public sealed class GMFloatingBall : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private RectTransform _rt;
        private RectTransform _parentRt;
        private bool _dragged;
        private Action _onClick;
        private Coroutine _snapCo;

        public static GMFloatingBall Create(Transform parent, Action onClick)
        {
            var go = new GameObject("GMFloatingBall", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(120, 120);
            rt.anchoredPosition = new Vector2(20, -200);

            BuildStyledBall(go);

            var ball = go.AddComponent<GMFloatingBall>();
            ball._rt = rt;
            ball._parentRt = parent as RectTransform;
            ball._onClick = onClick;

            return ball;
        }

        private static void BuildStyledBall(GameObject root)
        {
            var shadow = CreateLayer(root.transform, "Shadow", new Vector2(6, -6), new Vector2(120, 120));
            shadow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var outer = CreateLayer(root.transform, "Border", Vector2.zero, new Vector2(120, 120));
            outer.GetComponent<Image>().color = new Color32(56, 130, 246, 255);

            var inner = CreateLayer(outer.transform, "Fill", Vector2.zero, Vector2.zero);
            var innerRt = inner.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(4, 4);
            innerRt.offsetMax = new Vector2(-4, -4);
            inner.GetComponent<Image>().color = new Color32(24, 26, 36, 240);

            var highlight = CreateLayer(outer.transform, "Highlight", Vector2.zero, Vector2.zero);
            var hlRt = highlight.GetComponent<RectTransform>();
            hlRt.anchorMin = new Vector2(0, 0.5f);
            hlRt.anchorMax = new Vector2(1, 1);
            hlRt.offsetMin = new Vector2(6, 0);
            hlRt.offsetMax = new Vector2(-6, -6);
            highlight.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(outer.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (GMUGUIPanel.OverrideFont != null)
                text.font = GMUGUIPanel.OverrideFont;
            text.fontSize = 38;
            text.fontStyle = FontStyles.Normal;
            text.color = new Color32(140, 200, 255, 255);
            text.alignment = TextAlignmentOptions.Center;
            text.text = "GM";
        }

        private static GameObject CreateLayer(Transform parent, string name, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            go.AddComponent<Image>();
            return go;
        }

        public void OnDrag(PointerEventData eventData)
        {
            _dragged = true;
            if (_snapCo != null) { StopCoroutine(_snapCo); _snapCo = null; }
            _rt.anchoredPosition = ClampPosition(_rt.anchoredPosition + eventData.delta / GetCanvasScale());
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _snapCo = StartCoroutine(SnapToEdge());
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_dragged)
            {
                _dragged = false;
                return;
            }
            _onClick?.Invoke();
        }

        private Vector2 ClampPosition(Vector2 pos)
        {
            if (_parentRt == null) return pos;
            var size = _parentRt.rect.size;
            var ballSize = _rt.sizeDelta;
            pos.x = Mathf.Clamp(pos.x, 0, size.x - ballSize.x);
            pos.y = Mathf.Clamp(pos.y, -(size.y - ballSize.y), 0);
            return pos;
        }

        private System.Collections.IEnumerator SnapToEdge()
        {
            if (_parentRt == null) yield break;
            var parentW = _parentRt.rect.width;
            var ballW = _rt.sizeDelta.x;
            var pos = _rt.anchoredPosition;
            var centerX = pos.x + ballW * 0.5f;
            var targetX = centerX < parentW * 0.5f ? 0f : parentW - ballW;

            var from = pos;
            var to = new Vector2(targetX, pos.y);
            const float duration = 0.15f;
            float t = 0;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _rt.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t / duration));
                yield return null;
            }
            _rt.anchoredPosition = to;
            _snapCo = null;
        }

        private float GetCanvasScale()
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.scaleFactor : 1f;
        }
    }
}
#endif
