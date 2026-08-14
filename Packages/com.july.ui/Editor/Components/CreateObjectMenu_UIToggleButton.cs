using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Editor
{
    public static class CreateObjectMenu_UIToggleButton
    {
        private static readonly Color DefaultNormalColor = new(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color DefaultSelectedColor = new(0.3f, 0.7f, 1f, 1f);

        [MenuItem("GameObject/UI/UIToggleButton", false, 2032)]
        private static void CreateToggleButton(MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;

            var root = CreateUIObject("UIToggleButton", parent, new Vector2(160, 40));
            var normal = CreateUIObject("Normal", root, Vector2.zero);
            var selected = CreateUIObject("Selected", root, Vector2.zero);

            StretchToParent(normal.GetComponent<RectTransform>());
            StretchToParent(selected.GetComponent<RectTransform>());

            var rootImage = root.AddComponent<UIEmptyGraphic>();
            rootImage.raycastTarget = true;

            var normalImage = normal.AddComponent<Image>();
            normalImage.color = DefaultNormalColor;
            normalImage.raycastTarget = false;

            var selectedImage = selected.AddComponent<Image>();
            selectedImage.color = DefaultSelectedColor;
            selectedImage.raycastTarget = false;

            var toggle = root.AddComponent<UIToggleButton>();
            toggle.targetGraphic = rootImage;

            var so = new SerializedObject(toggle);
            so.FindProperty("_normal").objectReferenceValue = normal;
            so.FindProperty("_selected").objectReferenceValue = selected;
            so.ApplyModifiedPropertiesWithoutUndo();

            selected.SetActive(false);

            GameObjectUtility.SetParentAndAlign(root, parent);
            Undo.RegisterCreatedObjectUndo(root, "Create Toggle Button");
            Selection.activeGameObject = root;
        }

        private static GameObject CreateUIObject(string name, GameObject parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }

            if (size != Vector2.zero)
            {
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = size;
            }

            return go;
        }

        private static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
