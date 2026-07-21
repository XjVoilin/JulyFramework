using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Editor
{
    public static class CreateObjectMenu_UISmartButton
    {
        private static readonly Color DefaultButtonColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color DefaultTextColor = new Color(0.196f, 0.196f, 0.196f, 1f);

        [MenuItem("GameObject/UI/UISmartButton - TextMeshPro", false, 2031)]
        public static void AddSmartButton(MenuCommand menuCommand)
        {
            // 获取父对象（选中的对象或Canvas）
            var parent = GetOrCreateCanvasGameObject(menuCommand);

            // 创建按钮根对象
            var buttonObj = CreateUIElement("SmartButton", parent);

            // 添加 Image 组件
            var image = buttonObj.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = DefaultButtonColor;

            // 添加 SmartButton 组件
            buttonObj.AddComponent<UISmartButton>();

            // 设置按钮大小
            var buttonRect = buttonObj.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(160f, 40f);

            // 创建文本子对象
            var textObj = CreateUIElement("Text (TMP)", buttonObj);
            var textRect = textObj.GetComponent<RectTransform>();

            // 文本铺满按钮
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            // 添加 TextMeshProUGUI 组件
            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = "Button";
            text.color = DefaultTextColor;
            text.fontSize = 24;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            // 选中创建的对象
            Selection.activeGameObject = buttonObj;

            // 注册撤销
            Undo.RegisterCreatedObjectUndo(buttonObj, "Create SmartButton");
        }

        /// <summary>
        /// 创建一个带有 RectTransform 的 UI 元素
        /// </summary>
        private static GameObject CreateUIElement(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            // 设置层级
            go.layer = LayerMask.NameToLayer("UI");

            return go;
        }

        /// <summary>
        /// 获取或创建 Canvas
        /// </summary>
        private static GameObject GetOrCreateCanvasGameObject(MenuCommand menuCommand)
        {
            // 优先使用右键选中的对象
            var context = menuCommand.context as GameObject;
            if (context != null)
            {
                // 如果选中的对象有 Canvas 或在 Canvas 下
                var canvas = context.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    return context;
                }
            }

            // 查找场景中的 Canvas
            var existingCanvas = Object.FindFirstObjectByType<Canvas>();
            if (existingCanvas != null)
            {
                return existingCanvas.gameObject;
            }

            // 创建新的 Canvas
            var canvasObj = new GameObject("Canvas");
            var newCanvas = canvasObj.AddComponent<Canvas>();
            newCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            Undo.RegisterCreatedObjectUndo(canvasObj, "Create Canvas");

            // 创建 EventSystem（如果不存在）
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystemObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(eventSystemObj, "Create EventSystem");
            }

            return canvasObj;
        }
    }
}
