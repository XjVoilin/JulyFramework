using System;
using UnityEngine;
using UnityEngine.UI;

namespace July.Guide
{
    /// <summary>Default appearance, also usable as an authored prefab with serialized UGUI references.</summary>
    public sealed class DefaultGuideUguiSkin : GuideUguiSkin
    {
        [SerializeField] private Image _fullMask;
        [SerializeField] private Image[] _maskParts = new Image[4];
        [SerializeField] private Text _message;
        [SerializeField] private Text _pointer;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Text _confirmLabel;
        [SerializeField] private Button _skipButton;
        [SerializeField] private Text _skipLabel;
        [SerializeField] private Color _maskColor = new(0f, 0f, 0f, 0.68f);
        [SerializeField] private Vector2 _messageSizePixels = new(520f, 140f);
        [SerializeField] private Vector2 _messageOffsetPixels = new(280f, 84f);
        private GuideViewData _data;
        private bool _validated;
        private Canvas _canvas;
        private readonly Vector3[] _contentCorners = new Vector3[4];

        public override Graphic RaycastSurface => _fullMask;

        public override void Configure(GuideViewData data, string message, string skipLabel, bool canSkip)
        {
            if (!_validated)
            {
                ValidatePrefab();
                _message.raycastTarget = false;
                _pointer.raycastTarget = false;
                _confirmLabel.raycastTarget = false;
                _skipLabel.raycastTarget = false;
                _validated = true;
            }
            _data = data;
            _message.text = message;
            _message.gameObject.SetActive(!string.IsNullOrEmpty(message));
            _skipLabel.text = skipLabel;
            _skipButton.gameObject.SetActive(canSkip);
            foreach (var mask in _maskParts)
            {
                mask.color = _maskColor;
                mask.raycastTarget = false;
            }
            _pointer.text = data.PointerType == GuidePointerTypes.Drag ? "↔" : "●";
        }

        public override void SetTargetRect(Rect? targetRect)
        {
            var rect = targetRect.GetValueOrDefault();
            var visualHole = _data.MaskType == GuideMaskTypes.Rectangle && targetRect.HasValue;
            _fullMask.color = visualHole || _data.MaskType == GuideMaskTypes.None ? Color.clear : _maskColor;
            foreach (var mask in _maskParts)
                mask.gameObject.SetActive(visualHole);
            if (visualHole)
            {
                var xMin = Mathf.Clamp(rect.xMin, 0f, Screen.width);
                var xMax = Mathf.Clamp(rect.xMax, 0f, Screen.width);
                var yMin = Mathf.Clamp(rect.yMin, 0f, Screen.height);
                var yMax = Mathf.Clamp(rect.yMax, 0f, Screen.height);
                SetScreenRect(_maskParts[0].rectTransform, Rect.MinMaxRect(0f, 0f, xMin, Screen.height));
                SetScreenRect(_maskParts[1].rectTransform, Rect.MinMaxRect(xMax, 0f, Screen.width, Screen.height));
                SetScreenRect(_maskParts[2].rectTransform, Rect.MinMaxRect(xMin, 0f, xMax, yMin));
                SetScreenRect(_maskParts[3].rectTransform, Rect.MinMaxRect(xMin, yMax, xMax, Screen.height));
            }

            var position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (targetRect.HasValue)
            {
                switch (_data.PlacementType)
                {
                    case GuidePlacementTypes.Top: position = new Vector2(rect.center.x, rect.yMax + _messageOffsetPixels.y); break;
                    case GuidePlacementTypes.Bottom: position = new Vector2(rect.center.x, rect.yMin - _messageOffsetPixels.y); break;
                    case GuidePlacementTypes.Left: position = new Vector2(rect.xMin - _messageOffsetPixels.x, rect.center.y); break;
                    case GuidePlacementTypes.Right: position = new Vector2(rect.xMax + _messageOffsetPixels.x, rect.center.y); break;
                    case GuidePlacementTypes.Center: position = rect.center; break;
                }
            }
            ((RectTransform)_message.transform.parent).GetWorldCorners(_contentCorners);
            var safeMin = RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, _contentCorners[0]);
            var safeMax = RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, _contentCorners[2]);
            var safe = Rect.MinMaxRect(safeMin.x, safeMin.y, safeMax.x, safeMax.y);
            var halfWidth = Mathf.Min(_messageSizePixels.x * 0.5f, safe.width * 0.5f);
            var halfHeight = Mathf.Min(_messageSizePixels.y * 0.5f, safe.height * 0.5f);

            position.x = Mathf.Clamp(position.x, safe.xMin + halfWidth, safe.xMax - halfWidth);
            position.y = Mathf.Clamp(position.y, safe.yMin + halfHeight, safe.yMax - halfHeight);
            var halfSize = new Vector2(halfWidth, halfHeight);
            SetScreenRect(_message.rectTransform, new Rect(position - halfSize, halfSize * 2f));
            _pointer.gameObject.SetActive(_data.PointerType != GuidePointerTypes.None && targetRect.HasValue);
            SetScreenPosition(_pointer.rectTransform, rect.center);
        }

        public override void ShowConfirmation(bool visible, bool interactable, string label = null)
        {
            if (label != null)
                _confirmLabel.text = label;
            _confirmButton.interactable = interactable;
            _confirmButton.gameObject.SetActive(visible);
        }

        private void OnEnable()
        {
            _confirmButton.onClick.AddListener(ReportConfirmation);
            _skipButton.onClick.AddListener(ReportSkip);
        }

        private void OnDisable()
        {
            _confirmButton.onClick.RemoveListener(ReportConfirmation);
            _skipButton.onClick.RemoveListener(ReportSkip);
        }

        private void ValidatePrefab()
        {
            if (_fullMask == null || _message == null || _pointer == null || _confirmButton == null ||
                _confirmLabel == null || _skipButton == null || _skipLabel == null)
                throw new InvalidOperationException($"Guide skin {name} requires all serialized UGUI references.");
            // Presentation configuration runs before its owner hierarchy is activated.
            _canvas = GetComponentInParent<Canvas>(true)?.rootCanvas;
            if (_canvas == null || _canvas.renderMode != RenderMode.ScreenSpaceCamera || _canvas.worldCamera == null)
                throw new InvalidOperationException($"Default guide skin {name} requires the July UI camera Canvas.");
            if (_canvas.GetComponent<GraphicRaycaster>() == null)
                throw new InvalidOperationException($"Default guide skin {name} requires a GraphicRaycaster on its root Canvas.");
            if (!_confirmButton.enabled || !_skipButton.enabled || _confirmButton == _skipButton)
                throw new InvalidOperationException($"Default guide skin {name} requires distinct enabled confirmation and skip Buttons.");
            if (_maskParts == null || _maskParts.Length != 4)
                throw new InvalidOperationException($"Guide skin {name} requires four rectangular mask parts.");
            foreach (var mask in _maskParts)
                if (mask == null)
                    throw new InvalidOperationException($"Guide skin {name} has an unassigned mask part.");
            if (_messageSizePixels.x <= 0f || _messageSizePixels.y <= 0f)
                throw new InvalidOperationException($"Guide skin {name} requires a positive message size.");
        }

        private void SetScreenRect(RectTransform rectTransform, Rect rect)
        {
            var parent = (RectTransform)rectTransform.parent;
            var minimum = ScreenToLocal(parent, rect.min);
            var maximum = ScreenToLocal(parent, rect.max);
            SetLocalPosition(rectTransform, parent, (minimum + maximum) * 0.5f);
            rectTransform.sizeDelta = maximum - minimum;
        }

        private void SetScreenPosition(RectTransform rectTransform, Vector2 position)
        {
            var parent = (RectTransform)rectTransform.parent;
            SetLocalPosition(rectTransform, parent, ScreenToLocal(parent, position));
        }

        private Vector2 ScreenToLocal(RectTransform parent, Vector2 screenPoint)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, _canvas.worldCamera, out var point))
                throw new InvalidOperationException($"Guide skin parent {parent.name} cannot project a screen point.");
            return point;
        }

        private static void SetLocalPosition(RectTransform rectTransform, RectTransform parent, Vector2 position)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = parent.pivot;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
        }
    }
}
