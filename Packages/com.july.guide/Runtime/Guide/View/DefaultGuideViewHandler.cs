using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.UI;
using UnityEngine;
using UnityEngine.UI;

namespace July.Guide
{
    internal sealed class DefaultGuideViewHandler : IGuideViewHandler
    {
        private static readonly Color MaskColor = new(0f, 0f, 0f, 0.68f);
        private static readonly Color ClearColor = new(0f, 0f, 0f, 0f);

        private readonly Image[] _maskParts = new Image[4];
        private GameObject _root;
        private Image _fullMask;
        private Text _message;
        private Text _pointer;
        private Button _skipButton;

        public int Type => GuideBuiltInTypes.DefaultView;

        public UniTask OpenAsync(GuideViewContext context, GuideViewData data,
            CancellationToken cancellationToken)
        {
            Validate(data, context.InputMode);
            EnsureCreated();
            _root.SetActive(true);
            ConfigureMasks(context, data);
            ConfigureMessage(context, data);
            ConfigurePointer(context, data);
            ConfigureSkip(context);
            return UniTask.CompletedTask;
        }

        public UniTask CloseAsync(CancellationToken cancellationToken)
        {
            if (_root == null)
                return UniTask.CompletedTask;
            _root.SetActive(false);
            _skipButton.onClick.RemoveAllListeners();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }

        private void EnsureCreated()
        {
            if (_root != null)
                return;

            _root = new GameObject("JulyGuideView", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)UILayer.Guide;

            _fullMask = CreateImage("FullMask", _root.transform);
            Stretch(_fullMask.rectTransform);
            for (var i = 0; i < _maskParts.Length; i++)
                _maskParts[i] = CreateImage($"MaskPart-{i}", _root.transform);

            _message = CreateText("Message", _root.transform, 28, TextAnchor.MiddleCenter);
            _message.color = Color.white;
            _message.rectTransform.sizeDelta = new Vector2(520f, 140f);

            _pointer = CreateText("Pointer", _root.transform, 42, TextAnchor.MiddleCenter);
            _pointer.color = new Color(1f, 0.84f, 0.2f, 1f);
            _pointer.rectTransform.sizeDelta = new Vector2(64f, 64f);

            var skipImage = CreateImage("Skip", _root.transform);
            skipImage.color = new Color(0.15f, 0.15f, 0.15f, 0.92f);
            skipImage.rectTransform.anchorMin = skipImage.rectTransform.anchorMax = new Vector2(1f, 1f);
            skipImage.rectTransform.pivot = new Vector2(1f, 1f);
            skipImage.rectTransform.anchoredPosition = new Vector2(-32f, -32f);
            skipImage.rectTransform.sizeDelta = new Vector2(132f, 56f);
            _skipButton = skipImage.gameObject.AddComponent<Button>();
            var skipText = CreateText("Label", skipImage.transform, 24, TextAnchor.MiddleCenter);
            skipText.text = "Skip";
            Stretch(skipText.rectTransform);

            _root.SetActive(false);
        }

        private void ConfigureMasks(GuideViewContext context, GuideViewData data)
        {
            var visualHole = data.MaskType == GuideMaskTypes.Rectangle && context.TargetId != 0;
            var inputHole = context.InputMode == GuideInputModes.BlockOutsideTarget && context.TargetId != 0;
            var useParts = visualHole || inputHole;
            _fullMask.gameObject.SetActive(!useParts || context.InputMode == GuideInputModes.BlockAll);
            _fullMask.color = visualHole ? ClearColor : data.MaskType == GuideMaskTypes.None ? ClearColor : MaskColor;
            _fullMask.raycastTarget = context.InputMode != GuideInputModes.AllowAll;

            foreach (var part in _maskParts)
                part.gameObject.SetActive(useParts);
            if (!useParts)
                return;

            var rect = context.TargetRect;
            var color = visualHole ? MaskColor : ClearColor;
            SetScreenRect(_maskParts[0].rectTransform, Rect.MinMaxRect(0f, 0f, rect.xMin, Screen.height));
            SetScreenRect(_maskParts[1].rectTransform, Rect.MinMaxRect(rect.xMax, 0f, Screen.width, Screen.height));
            SetScreenRect(_maskParts[2].rectTransform, Rect.MinMaxRect(rect.xMin, 0f, rect.xMax, rect.yMin));
            SetScreenRect(_maskParts[3].rectTransform, Rect.MinMaxRect(rect.xMin, rect.yMax, rect.xMax, Screen.height));
            foreach (var part in _maskParts)
            {
                part.color = color;
                part.raycastTarget = context.InputMode == GuideInputModes.BlockOutsideTarget;
            }
        }

        private void ConfigureMessage(GuideViewContext context, GuideViewData data)
        {
            _message.gameObject.SetActive(!string.IsNullOrEmpty(data.TextKey));
            _message.text = data.TextKey;
            var position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (context.TargetId != 0)
            {
                var rect = context.TargetRect;
                const float gap = 84f;
                switch (data.PlacementType)
                {
                    case GuidePlacementTypes.Top: position = new Vector2(rect.center.x, rect.yMax + gap); break;
                    case GuidePlacementTypes.Bottom: position = new Vector2(rect.center.x, rect.yMin - gap); break;
                    case GuidePlacementTypes.Left: position = new Vector2(rect.xMin - 280f, rect.center.y); break;
                    case GuidePlacementTypes.Right: position = new Vector2(rect.xMax + 280f, rect.center.y); break;
                    case GuidePlacementTypes.Center: position = rect.center; break;
                    default: throw new ArgumentOutOfRangeException();
                }
            }
            position.x = Mathf.Clamp(position.x, 260f, Screen.width - 260f);
            position.y = Mathf.Clamp(position.y, 70f, Screen.height - 70f);
            SetScreenPosition(_message.rectTransform, position);
        }

        private void ConfigurePointer(GuideViewContext context, GuideViewData data)
        {
            var visible = data.PointerType != GuidePointerTypes.None && context.TargetId != 0;
            _pointer.gameObject.SetActive(visible);
            if (!visible)
                return;
            _pointer.text = data.PointerType == GuidePointerTypes.Drag ? "↔" : "●";
            SetScreenPosition(_pointer.rectTransform, context.TargetRect.center);
        }

        private void ConfigureSkip(GuideViewContext context)
        {
            _skipButton.gameObject.SetActive(context.CanSkip);
            _skipButton.onClick.RemoveAllListeners();
            if (context.CanSkip)
                _skipButton.onClick.AddListener(() => context.SkipAsync().Forget(Debug.LogException));
        }

        private static void Validate(GuideViewData data, int inputMode)
        {
            if (data.MaskType != GuideMaskTypes.None && data.MaskType != GuideMaskTypes.Rectangle)
                throw new InvalidOperationException($"Default guide view does not support mask type {data.MaskType}.");
            if (data.PointerType != GuidePointerTypes.None && data.PointerType != GuidePointerTypes.Click &&
                data.PointerType != GuidePointerTypes.Drag)
                throw new InvalidOperationException($"Default guide view does not support pointer type {data.PointerType}.");
            if (data.PlacementType < GuidePlacementTypes.Center || data.PlacementType > GuidePlacementTypes.Right)
                throw new InvalidOperationException($"Default guide view does not support placement type {data.PlacementType}.");
            if (inputMode != GuideInputModes.BlockAll && inputMode != GuideInputModes.BlockOutsideTarget &&
                inputMode != GuideInputModes.AllowAll)
                throw new InvalidOperationException($"Default guide view does not support input mode {inputMode}.");
        }

        private static Image CreateImage(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<Image>();
        }

        private static Text CreateText(string name, Transform parent, int fontSize, TextAnchor alignment)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void SetScreenRect(RectTransform rectTransform, Rect rect)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = rect.center;
            rectTransform.sizeDelta = rect.size;
        }

        private static void SetScreenPosition(RectTransform rectTransform, Vector2 position)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
        }
    }
}
