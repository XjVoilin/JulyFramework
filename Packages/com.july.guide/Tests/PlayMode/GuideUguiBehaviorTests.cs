#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.UI;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.Guide.Validation
{
    public sealed class GuideUguiBehaviorTests
    {
        private readonly List<GameObject> _ownedRoots = new();
        private ArchContext _architecture;
        private GuideStore _store;
        private UiGuideSystem _guide;
        private EventSystem _events;
        private Canvas _canvas;
        private UISystem _ui;
        private GuideWindowResources _resources;
        private GuideWindowProvider _provider;

        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(ArchContext.Current, Is.Null, "UI tests require an isolated scene without a running game Architecture.");
            Assert.That(EventSystem.current, Is.Null, "UI tests require an isolated scene without another EventSystem.");
            _architecture = new ArchContext();
            _store = new GuideStore();
            _guide = new UiGuideSystem();
            _resources = new GuideWindowResources();
            _provider = new GuideWindowProvider();
            _ui = new UISystem();
            var uiConfig = UIConfig.Default;
            uiConfig.DesignResolution = new Vector2Int(Screen.width / 2, Screen.height / 2);
            _ui.Configure(uiConfig);
            _ui.SetMainProvider(_provider);
            _architecture.RegisterSystem(_resources);
            _architecture.RegisterSystem(_ui);
            _architecture.RegisterStore(_store);
            _architecture.RegisterSystem(_guide);
            await _architecture.InitializeAsync();

            _events = EventSystem.current;
            var canvasRoot = new GameObject("GuideValidationCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            _ownedRoots.Add(canvasRoot);
            _canvas = canvasRoot.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _ui.UICamera;
            _canvas.planeDistance = 100;
            _canvas.sortingOrder = 0;
            await LayoutFrame();
        });

        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            try
            {
                if (_guide != null && _guide.IsRunning)
                    await _guide.StopAsync();
            }
            finally
            {
                foreach (var root in _ownedRoots)
                    if (root != null)
                        Object.Destroy(root);
                _ownedRoots.Clear();
                _architecture?.Shutdown();
                Assert.That(_resources.ActiveHandles, Is.Zero);
                SafeAreaAdapter.SafeAreaOverride = null;
                _architecture = null;
                _guide = null;
                _store = null;
                await UniTask.NextFrame();
            }
        });

        [UnityTest]
        public IEnumerator PassiveChildAnchor_PreservesParentButtonClickRouting() => UniTask.ToCoroutine(async () =>
        {
            var button = CreateButton("BusinessButton", _canvas.transform, new Vector2(160f, 80f));
            var label = CreateImage("PassiveHighlightedChild", button.transform);
            Stretch(label.rectTransform);
            var target = AddConfiguredTarget<GuideUITarget>(label.gameObject, 101);
            var accepted = 0;
            button.onClick.AddListener(() => accepted++);
            button.gameObject.SetActive(true);
            await LayoutFrame();

            var hit = Raycast(target.ScreenRect.center);
            Assert.That(hit, Is.EqualTo(label.gameObject));
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit), Is.EqualTo(button.gameObject),
                "Adding a geometry-only guide anchor must not intercept its ancestor's clicks.");
            Click(target.ScreenRect.center);
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(_guide.IsRunning, Is.False);
        });

        [UnityTest]
        public IEnumerator DisabledButton_DoesNotCompleteUntilItAcceptsARealClick() => UniTask.ToCoroutine(async () =>
        {
            var button = CreateButton("BusinessButton", _canvas.transform, new Vector2(160f, 80f));
            var target = AddConfiguredTarget<GuideButtonTarget>(button.gameObject, 102);
            button.interactable = false;
            var accepted = 0;
            button.onClick.AddListener(() => accepted++);
            _guide.Factory = context => new GuideClickTargetProcedure(context, 900, 102, new GuideViewData("Click"));
            var run = _guide.RunAsync();
            await LayoutFrame();

            Click(target.ScreenRect.center);
            await LayoutFrame();
            Assert.That(accepted, Is.Zero);
            Assert.That(_guide.IsRunning, Is.True);
            Assert.That(_store.IsFinished(1), Is.False);

            button.interactable = true;
            Click(target.ScreenRect.center);
            Assert.That(await run.Timeout(TimeSpan.FromSeconds(5)), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(_store.IsCompleted(1), Is.True);
            await AssertPresentationReleased();
        });

        [UnityTest]
        public IEnumerator AcceptedClick_StillCompletesWhenEarlierBusinessListenerClosesWindow() => UniTask.ToCoroutine(async () =>
        {
            var window = new GameObject("BusinessWindow", typeof(RectTransform));
            window.transform.SetParent(_canvas.transform, false);
            Stretch((RectTransform)window.transform);
            var button = CreateButton("CloseWindowButton", window.transform, new Vector2(160f, 80f));
            var accepted = 0;
            // Register the real business action before the adapter's OnEnable subscribes.
            button.onClick.AddListener(() =>
            {
                accepted++;
                window.SetActive(false);
            });
            var target = AddConfiguredTarget<GuideButtonTarget>(button.gameObject, 103);
            _guide.Factory = context => new GuideClickTargetProcedure(context, 900, 103, new GuideViewData("Close window"));
            var run = _guide.RunAsync();
            await LayoutFrame();

            Click(target.ScreenRect.center);
            Assert.That(await run.Timeout(TimeSpan.FromSeconds(5)), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(window.activeSelf, Is.False);
            Assert.That(_guide.LastFailure, Is.Null);
            Assert.That(_store.IsCompleted(1), Is.True);
            await AssertPresentationReleased();
        });

        [UnityTest]
        public IEnumerator TargetDisappearsWithoutActivation_FaultsAndReleasesPresentation() => UniTask.ToCoroutine(async () =>
        {
            var button = CreateButton("DisappearingButton", _canvas.transform, new Vector2(160f, 80f));
            AddConfiguredTarget<GuideButtonTarget>(button.gameObject, 104);
            _guide.Factory = context => new GuideClickTargetProcedure(context, 900, 104, new GuideViewData("Click"));
            var run = _guide.RunAsync();
            await LayoutFrame();
            Assert.That(Object.FindObjectsOfType<GuideUguiSkin>(), Has.Length.EqualTo(1));

            button.gameObject.SetActive(false);
            InvalidOperationException failure = null;
            try
            {
                await run.Timeout(TimeSpan.FromSeconds(5));
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null, "Losing an unactivated target must fail the real run.");
            Assert.That(failure.Message, Does.Contain("104").And.Contain("disappeared"));
            Assert.That(_guide.LastFailure, Is.SameAs(failure));
            Assert.That(_guide.IsRunning, Is.False);
            Assert.That(_store.IsFinished(1), Is.False);
            await AssertPresentationReleased();
        });

        [UnityTest]
        public IEnumerator Confirmation_WaitsForItsRenderedButtonAndThenReleasesPresentation() => UniTask.ToCoroutine(async () =>
        {
            _guide.Factory = context => new GuideConfirmProcedure(context, 900,
                new GuideViewData("Read this instruction", confirmTextKey: "Accept instruction"));
            var run = _guide.RunAsync();
            await LayoutFrame();
            Assert.That(_guide.IsRunning, Is.True);
            Assert.That(_store.IsFinished(1), Is.False);
            Assert.That(_guide.WaitingFor, Is.EqualTo("Confirmation"));

            var confirm = FindButtonWithLabel("Accept instruction");
            Click(ScreenRect((RectTransform)confirm.transform).center);
            Assert.That(await run.Timeout(TimeSpan.FromSeconds(5)), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(_store.IsCompleted(1), Is.True);
            await AssertPresentationReleased();
        });

        [UnityTest]
        public IEnumerator AuthoredScaledSkin_TracksScreenGeometryAndAllowsOnlyTargetRaycasts() => UniTask.ToCoroutine(async () =>
        {
            var button = CreateButton("MovingBusinessButton", _canvas.transform,
                new Vector2(Screen.width * 0.18f, Screen.height * 0.12f));
            var target = AddConfiguredTarget<GuideButtonTarget>(button.gameObject, 105);
            var template = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GuideWindowResources.PrefabPath));
            template.SetActive(false);
            _ownedRoots.Add(template);
            _resources.Template = template;
            var templateSkin = template.GetComponentInChildren<DefaultGuideUguiSkin>(true);
            templateSkin.transform.Find("SafeContent/Message").GetComponent<Text>().color = Color.magenta;
            _guide.Factory = context => new GuideClickTargetProcedure(context, 900, 105,
                new GuideViewData("Custom appearance", GuideMaskTypes.Rectangle, GuidePointerTypes.Click,
                    GuidePlacementTypes.Top));
            var run = _guide.RunAsync();
            await LayoutFrame();

            var activeSkins = Object.FindObjectsOfType<DefaultGuideUguiSkin>();
            Assert.That(activeSkins, Has.Length.EqualTo(1));
            var clone = activeSkins[0];
            Assert.That(clone, Is.Not.SameAs(templateSkin));
            Assert.That(clone.GetComponentInParent<Canvas>().scaleFactor, Is.EqualTo(2f));
            Assert.That(clone.transform.Find("SafeContent/Message").GetComponent<Text>().color, Is.EqualTo(Color.magenta));
            AssertHoleAndPointerMatchTarget(clone, target.ScreenRect);
            Assert.That(Raycast(target.ScreenRect.center), Is.EqualTo(button.gameObject));
            Assert.That(Raycast(new Vector2(5f, 5f)), Is.EqualTo(clone.RaycastSurface.gameObject));

            var originalPosition = target.ScreenRect.center;
            ((RectTransform)button.transform).anchoredPosition += new Vector2(Screen.width * 0.25f, Screen.height * 0.05f);
            await LayoutFrame();
            AssertHoleAndPointerMatchTarget(clone, target.ScreenRect);
            Assert.That(Raycast(target.ScreenRect.center), Is.EqualTo(button.gameObject));
            Assert.That(Raycast(originalPosition), Is.EqualTo(clone.RaycastSurface.gameObject));

            Click(target.ScreenRect.center);
            Assert.That(await run.Timeout(TimeSpan.FromSeconds(5)), Is.EqualTo(GuideExitReason.Completed));
            await AssertPresentationReleased(templateSkin);
            Assert.That(template != null, Is.True, "Disposing a presentation must not destroy its authored template.");
            Assert.That(template.gameObject.activeSelf, Is.False);
        });

        [UnityTest]
        public IEnumerator ExternalWindowClose_FaultsInsteadOfLeavingConfirmationWaiting() => UniTask.ToCoroutine(async () =>
        {
            _guide.Factory = context => new GuideConfirmProcedure(context, 900, new GuideViewData("Confirm"));
            var run = _guide.RunAsync();
            await LayoutFrame();
            var window = Object.FindObjectOfType<GuideWindow>();
            Assert.That(window, Is.Not.Null);
            await _ui.CloseAsync(window);
            Exception failure = null;
            try { await run.Timeout(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException error) { failure = error; }
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Message, Does.Contain("closed before"));
            Assert.That(_store.IsFinished(1), Is.False);
            await AssertPresentationReleased();
        });

        [UnityTest]
        public IEnumerator Stop_WaitsForRealUiCloseAnimationBeforePublishingExit() => UniTask.ToCoroutine(async () =>
        {
            _provider.CloseAnimation = UIAnimationType.Fade;
            _guide.Factory = context => new GuideConfirmProcedure(context, 900, new GuideViewData("Confirm"));
            var run = _guide.RunAsync();
            await LayoutFrame();
            var window = Object.FindObjectOfType<GuideWindow>();
            var stop = _guide.StopAsync();
            Assert.That(stop.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(_guide.IsRunning, Is.True);
            Assert.That(window.IsOpened, Is.False);
            await stop.Timeout(TimeSpan.FromSeconds(5));
            Assert.That(await run, Is.EqualTo(GuideExitReason.Aborted));
            Assert.That(_store.IsFinished(1), Is.False);
            await AssertPresentationReleased();
        });

        [UnityTest]
        public IEnumerator StopDuringResourceLoad_ReleasesWithoutOpeningGuideWindow() => UniTask.ToCoroutine(async () =>
        {
            _resources.DelayFrames = 3;
            _guide.Factory = context => new GuideConfirmProcedure(context, 900, new GuideViewData("Confirm"));
            var run = _guide.RunAsync();
            await _guide.StopAsync().Timeout(TimeSpan.FromSeconds(5));
            Assert.That(await run, Is.EqualTo(GuideExitReason.Aborted));
            Assert.That(_resources.ActiveHandles, Is.Zero);
            Assert.That(Object.FindObjectsOfType<GuideWindow>(), Is.Empty);
        });

        [UnityTest]
        public IEnumerator WindowUsesUiGuideLayer_WithFullScreenMaskAndSafeAreaControls() => UniTask.ToCoroutine(async () =>
        {
            var safe = new Rect(Screen.width*.1f, Screen.height*.1f, Screen.width*.8f, Screen.height*.8f);
            SafeAreaAdapter.SafeAreaOverride = () => safe;
            _guide.Factory = context => new GuideConfirmProcedure(context, 900, new GuideViewData("Confirm"));
            var run = _guide.RunAsync();
            await LayoutFrame();
            var skin = Object.FindObjectOfType<DefaultGuideUguiSkin>();
            var canvas = skin.GetComponentInParent<Canvas>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(canvas.worldCamera, Is.SameAs(_ui.UICamera));
            Assert.That(canvas.sortingOrder, Is.EqualTo((int)UILayer.Guide));
            var mask = ScreenRect(skin.RaycastSurface.rectTransform);
            Assert.That(mask.xMin, Is.EqualTo(0).Within(1));
            Assert.That(mask.yMin, Is.EqualTo(0).Within(1));
            Assert.That(mask.width, Is.EqualTo(Screen.width).Within(1));
            Assert.That(mask.height, Is.EqualTo(Screen.height).Within(1));
            var content = ScreenRect((RectTransform)skin.transform.Find("SafeContent"));
            Assert.That(content.xMin, Is.EqualTo(safe.xMin).Within(1));
            Assert.That(content.yMax, Is.EqualTo(safe.yMax).Within(1));
            await _guide.StopAsync();
            Assert.That(await run, Is.EqualTo(GuideExitReason.Aborted));
            await AssertPresentationReleased();
        });

        private static void AssertHoleAndPointerMatchTarget(DefaultGuideUguiSkin skin, Rect target)
        {
            var left = ScreenRect((RectTransform)skin.transform.Find("MaskPart-0"));
            var right = ScreenRect((RectTransform)skin.transform.Find("MaskPart-1"));
            var bottom = ScreenRect((RectTransform)skin.transform.Find("MaskPart-2"));
            var top = ScreenRect((RectTransform)skin.transform.Find("MaskPart-3"));
            Assert.That(left.xMax, Is.EqualTo(target.xMin).Within(1f), "Left edge of the screen-space mask hole.");
            Assert.That(right.xMin, Is.EqualTo(target.xMax).Within(1f), "Right edge of the screen-space mask hole.");
            Assert.That(bottom.yMax, Is.EqualTo(target.yMin).Within(1f), "Bottom edge of the screen-space mask hole.");
            Assert.That(top.yMin, Is.EqualTo(target.yMax).Within(1f), "Top edge of the screen-space mask hole.");
            var pointer = RectTransformUtility.WorldToScreenPoint(skin.GetComponentInParent<Canvas>().worldCamera, skin.transform.Find("Pointer").position);
            Assert.That(Vector2.Distance(pointer, target.center), Is.LessThan(1f));
        }

        private static Rect ScreenRect(RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var minimum = RectTransformUtility.WorldToScreenPoint(rectTransform.GetComponentInParent<Canvas>().worldCamera, corners[0]);
            var maximum = RectTransformUtility.WorldToScreenPoint(rectTransform.GetComponentInParent<Canvas>().worldCamera, corners[2]);
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private static Button FindButtonWithLabel(string label)
        {
            foreach (var button in Object.FindObjectsOfType<Button>())
            {
                var text = button.GetComponentInChildren<Text>();
                if (text != null && text.text == label)
                    return button;
            }
            Assert.Fail("No active real Button renders the expected label: " + label);
            return null;
        }

        private static async UniTask AssertPresentationReleased(GuideUguiSkin retainedTemplate = null)
        {
            await LayoutFrame();
            var remaining = Object.FindObjectsOfType<GuideUguiSkin>(true);
            if (retainedTemplate == null)
                Assert.That(remaining, Is.Empty, "Inactive presentation instances must also be destroyed.");
            else
                Assert.That(remaining, Is.EqualTo(new[] { retainedTemplate }));
            foreach (var root in Object.FindObjectsOfType<Transform>(true))
                Assert.That(root.name, Is.Not.EqualTo("JulyGuidePresentation"),
                    "The controller root must be released, even if inactive.");
        }

        private static Text CreateText(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            child.transform.SetParent(parent, false);
            var text = child.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private Button CreateButton(string name, Transform parent, Vector2 size)
        {
            var image = CreateImage(name, parent);
            image.gameObject.SetActive(false);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static T AddConfiguredTarget<T>(GameObject owner, int targetId) where T : GuideTargetAnchor
        {
            owner.SetActive(false);
            var target = owner.AddComponent<T>();
            // This is the production Inspector/serialized-configuration surface, not runtime-state reflection.
            JsonUtility.FromJsonOverwrite("{\"_targetId\":" + targetId + "}", target);
            owner.SetActive(true);
            return target;
        }

        private static Image CreateImage(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<Image>();
            image.color = Color.white;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private GameObject Raycast(Vector2 position)
        {
            var pointer = new PointerEventData(_events) { position = position, button = PointerEventData.InputButton.Left };
            var results = new List<RaycastResult>();
            _events.RaycastAll(pointer, results);
            Assert.That(results, Is.Not.Empty, "The real UGUI raycaster must find a visible surface.");
            return results[0].gameObject;
        }

        private void Click(Vector2 position)
        {
            var hit = Raycast(position);
            var pointer = new PointerEventData(_events)
            {
                position = position,
                button = PointerEventData.InputButton.Left,
                eligibleForClick = true
            };
            var pressed = ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
            var clicked = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (pressed != null)
                ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerUpHandler);
            if (clicked != null)
                ExecuteEvents.Execute(clicked, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static async UniTask LayoutFrame()
        {
            await UniTask.NextFrame();
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            Canvas.ForceUpdateCanvases();
        }

        private sealed class UiGuideSystem : GuideSystemBase
        {
            internal Func<GuideStepContext, ProcedureBase> Factory;

            protected override IEnumerable<GuideDefinition> CreateGuides()
            {
                yield return new GuideDefinition(1, new[] { new GuideStepDefinition(1, 1) });
            }

            protected override bool CanStart(GuideDefinition guide) => true;
            protected override ProcedureBase CreateStepProcedure(GuideStepContext context) => Factory(context);
        }
    }
}

#endif
