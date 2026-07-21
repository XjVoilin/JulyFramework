using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Editor
{
    public static class CreateObjectMenu_UIPageNavigator
    {
        private static readonly Color BgColor = new(0.96f, 0.94f, 0.90f, 1f);
        private static readonly Color BtnColor = new(0.3f, 0.7f, 0.45f, 1f);
        private static readonly Color DotNormalColor = new(0.82f, 0.80f, 0.76f, 1f);
        private static readonly Color DotSelectedColor = new(0.35f, 0.65f, 0.50f, 1f);

        [MenuItem("GameObject/UI/UIPageNavigator", false, 2033)]
        private static void Create(MenuCommand cmd)
        {
            var parent = cmd.context as GameObject;

            var root = CreateUI("UIPageNavigator", parent, new Vector2(400, 520));
            var rootImg = root.AddComponent<Image>();
            rootImg.color = BgColor;
            rootImg.raycastTarget = true;

            var navigator = root.AddComponent<UIPageNavigator>();
            var slideTransition = root.AddComponent<PageTransitionSlide>();

            // --- Pages ---
            var pagesGo = CreateUI("Pages", root, Vector2.zero);
            var pagesRt = pagesGo.GetComponent<RectTransform>();
            pagesRt.anchorMin = new Vector2(0, 0.15f);
            pagesRt.anchorMax = new Vector2(1, 0.9f);
            pagesRt.offsetMin = new Vector2(16, 0);
            pagesRt.offsetMax = new Vector2(-16, 0);

            var page0 = CreatePage("Page_0", pagesGo, new Color(0.88f, 0.95f, 0.88f));
            var page1 = CreatePage("Page_1", pagesGo, new Color(0.88f, 0.90f, 0.95f));
            page1.SetActive(false);

            // --- Indicator ---
            var indicatorGo = CreateUI("Indicator", root, new Vector2(200, 20));
            var indicatorRt = indicatorGo.GetComponent<RectTransform>();
            indicatorRt.anchorMin = new Vector2(0.5f, 0.1f);
            indicatorRt.anchorMax = new Vector2(0.5f, 0.1f);
            indicatorRt.anchoredPosition = Vector2.zero;

            var layout = indicatorGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            var dotTemplate = CreateDot("DotTemplate", indicatorGo);
            var textIndicator = CreateTextIndicator("TextIndicator", indicatorGo);
            textIndicator.SetActive(false);

            // --- Buttons ---
            var btnPrev = CreateButton("BtnPrev", root, "\u25C0",
                new Vector2(0.1f, 0.02f), new Vector2(50, 40));
            var btnNext = CreateButton("BtnNext", root, "\u25B6",
                new Vector2(0.9f, 0.02f), new Vector2(50, 40));
            var btnClose = CreateButton("BtnClose", root, "\u77E5\u9053\u4E86",
                new Vector2(0.5f, 0.02f), new Vector2(120, 40));

            btnClose.GetComponentInChildren<Image>().color = BtnColor;

            // --- Wire fields ---
            var so = new SerializedObject(navigator);

            var pagesProp = so.FindProperty("_pages");
            pagesProp.arraySize = 2;
            pagesProp.GetArrayElementAtIndex(0).objectReferenceValue =
                page0.GetComponent<RectTransform>();
            pagesProp.GetArrayElementAtIndex(1).objectReferenceValue =
                page1.GetComponent<RectTransform>();

            so.FindProperty("_btnPrev").objectReferenceValue =
                btnPrev.GetComponent<UISmartButton>();
            so.FindProperty("_btnNext").objectReferenceValue =
                btnNext.GetComponent<UISmartButton>();
            so.FindProperty("_btnClose").objectReferenceValue =
                btnClose.GetComponent<UISmartButton>();
            so.FindProperty("_indicatorRoot").objectReferenceValue = indicatorRt;
            so.FindProperty("_dotTemplate").objectReferenceValue =
                dotTemplate.GetComponent<UIPageDot>();
            so.FindProperty("_textIndicator").objectReferenceValue =
                textIndicator.GetComponent<TextMeshProUGUI>();
            var transitionsProp = so.FindProperty("_transitions");
            transitionsProp.arraySize = 1;
            transitionsProp.GetArrayElementAtIndex(0).objectReferenceValue = slideTransition;

            so.ApplyModifiedPropertiesWithoutUndo();

            // --- Finalize ---
            GameObjectUtility.SetParentAndAlign(root, parent);
            Undo.RegisterCreatedObjectUndo(root, "Create UIPageNavigator");
            Selection.activeGameObject = root;
        }

        private static GameObject CreatePage(string name, GameObject parent, Color color)
        {
            var go = CreateUI(name, parent, Vector2.zero);
            Stretch(go.GetComponent<RectTransform>());

            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            return go;
        }

        private static GameObject CreateDot(string name, GameObject parent)
        {
            var go = CreateUI(name, parent, new Vector2(16, 16));
            var dot = go.AddComponent<UIPageDot>();

            var normal = CreateUI("Normal", go, Vector2.zero);
            Stretch(normal.GetComponent<RectTransform>());
            var nImg = normal.AddComponent<Image>();
            nImg.color = DotNormalColor;
            nImg.raycastTarget = false;

            var selected = CreateUI("Selected", go, Vector2.zero);
            Stretch(selected.GetComponent<RectTransform>());
            var sImg = selected.AddComponent<Image>();
            sImg.color = DotSelectedColor;
            sImg.raycastTarget = false;

            var dotSo = new SerializedObject(dot);
            dotSo.FindProperty("_normal").objectReferenceValue = normal;
            dotSo.FindProperty("_selected").objectReferenceValue = selected;
            dotSo.ApplyModifiedPropertiesWithoutUndo();

            return go;
        }

        private static GameObject CreateTextIndicator(string name, GameObject parent)
        {
            var go = CreateUI(name, parent, new Vector2(100, 30));
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = "1/2";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 18;
            text.color = DotSelectedColor;
            text.raycastTarget = false;
            return go;
        }

        private static GameObject CreateButton(string name, GameObject parent,
            string label, Vector2 anchorPos, Vector2 size)
        {
            var go = CreateUI(name, parent, size);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorPos;
            rt.anchorMax = anchorPos;
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = true;

            go.AddComponent<UISmartButton>();

            var textGo = CreateUI("Label", go, Vector2.zero);
            Stretch(textGo.GetComponent<RectTransform>());
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 18;
            text.color = Color.white;
            text.raycastTarget = false;

            return go;
        }

        private static GameObject CreateUI(string name, GameObject parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            if (parent) go.transform.SetParent(parent.transform, false);
            if (size != Vector2.zero)
                go.GetComponent<RectTransform>().sizeDelta = size;
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
