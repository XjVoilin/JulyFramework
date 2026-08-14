using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Editor
{
    public static class CreateObjectMenu_UIToggleGroup
    {
        private const int DefaultItemCount = 3;
        private static readonly Color NormalColor = new(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color SelectedColor = new(0.3f, 0.7f, 1f, 1f);
        private static readonly Color LockedColor = new(0.7f, 0.7f, 0.7f, 1f);

        [MenuItem("GameObject/UI/UIToggleGroup", false, 2033)]
        private static void CreateToggleGroup(MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;

            var root = CreateUIObject("UIToggleGroup", parent, new Vector2(400, 50));
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.spacing = 4;

            var group = root.AddComponent<UIToggleGroup>();
            var so = new SerializedObject(group);
            var itemsProp = so.FindProperty("_items");
            itemsProp.arraySize = DefaultItemCount;

            for (int i = 0; i < DefaultItemCount; i++)
            {
                var item = CreateItem(root, i);
                itemsProp.GetArrayElementAtIndex(i).objectReferenceValue = item;
            }

            so.FindProperty("_selectedIndex").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObjectUtility.SetParentAndAlign(root, parent);
            Undo.RegisterCreatedObjectUndo(root, "Create Toggle Group");
            Selection.activeGameObject = root;
        }

        private static UIToggleItem CreateItem(GameObject parent, int index)
        {
            var root = CreateUIObject($"Item_{index}", parent, Vector2.zero);

            var normal = CreateUIObject("Normal", root, Vector2.zero);
            var selected = CreateUIObject("Selected", root, Vector2.zero);
            var locked = CreateUIObject("Locked", root, Vector2.zero);

            StretchToParent(normal.GetComponent<RectTransform>());
            StretchToParent(selected.GetComponent<RectTransform>());
            StretchToParent(locked.GetComponent<RectTransform>());

            var rootImage = root.AddComponent<UIEmptyGraphic>();
            rootImage.raycastTarget = true;

            var normalImage = normal.AddComponent<Image>();
            normalImage.color = NormalColor;
            normalImage.raycastTarget = false;

            var selectedImage = selected.AddComponent<Image>();
            selectedImage.color = SelectedColor;
            selectedImage.raycastTarget = false;

            var lockedImage = locked.AddComponent<Image>();
            lockedImage.color = LockedColor;
            lockedImage.raycastTarget = false;

            var item = root.AddComponent<UIToggleItem>();
            item.targetGraphic = rootImage;

            var itemSo = new SerializedObject(item);
            itemSo.FindProperty("_normal").objectReferenceValue = normal;
            itemSo.FindProperty("_selected").objectReferenceValue = selected;
            itemSo.FindProperty("_locked").objectReferenceValue = locked;
            itemSo.ApplyModifiedPropertiesWithoutUndo();

            selected.SetActive(false);
            locked.SetActive(false);

            return item;
        }

        private static GameObject CreateUIObject(string name, GameObject parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

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
