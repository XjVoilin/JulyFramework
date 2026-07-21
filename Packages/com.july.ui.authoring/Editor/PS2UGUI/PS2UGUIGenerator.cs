using System;
using System.Collections.Generic;
using System.IO;
using July.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace July.UI.Authoring.Editor
{
    /// <summary>
    /// 从布局 JSON 一键生成 Unity UI Prefab
    /// 右键 _ps_data.json / _figma_data.json → PS2UGUI
    /// </summary>
    public static class PS2UGUIGenerator
    {
        // UITemplate.prefab 已不再需要，prefab 结构由代码直接创建
        private static string FontMaterialPath => PS2UGUISettings.GetFontMaterialPath();
        private static string DefaultFontAssetPath => PS2UGUISettings.GetDefaultFontAssetPath();
        private const int SlicePixelTolerance = 5;
        private const int SliceMinimumCenterPixelsPerSide = 2;
        private const float ScaleCompensationEpsilon = 0.0001f;
        private const float SerializedValueTruncateEpsilon = 0.000001f;
        private static readonly string[] SupportedJsonFileSuffixes = { "_ui_data.json", "_ps_data.json", "_figma_data.json" };
        private static readonly string[] PrefabNameSuffixes = { "_ui_data", "_ps_data", "_figma_data" };

        private const string FontMaterialPrefix = "Font_Main Material ";


        private static readonly Dictionary<int, int> OpacityMap = new Dictionary<int, int>
        {
            { 0, 0 }, { 5, -1 }, { 10, -1 }, { 15, -1 }, { 20, -1 },
            { 25, -1 }, { 30, -1 }, { 35, -1 }, { 40, -1 }, { 45, -1 },
            { 50, -1 }, { 55, -1 }, { 60, -1 }, { 65, -1 }, { 70, -1 },
            { 75, -1 }, { 80, -1 }, { 85, -1 }, { 90, -1 }, { 95, -1 },
            { 100, 100 }
        };

        // 运行时缓存：当前 JSON 所在目录（末尾含 /）
        private static string _currentJsonDirectory;

        #region 公开 API

        /// <summary>
        /// 程序化入口：从 JSON 文件生成 Prefab。
        /// 供 AI 自动化流程或外部脚本调用。
        /// </summary>
        /// <param name="jsonAssetPath">JSON 文件的 Asset 路径</param>
        public static void GenerateFromJson(string jsonAssetPath)
        {
            if (!IsSupportedPrefabJsonPath(jsonAssetPath))
            {
                Debug.LogError($"PS2UGUI: 不支持的文件路径 → {jsonAssetPath}");
                return;
            }

            UnityData data = PS2UGUITranslate.Translate(jsonAssetPath);

            if (data == null || data.canvas == null || data.children == null)
            {
                Debug.LogError($"PS2UGUI: JSON 转换失败 → {jsonAssetPath}");
                return;
            }

            var uiName = GetPrefabNameFromJsonPath(jsonAssetPath);
            GeneratePrefab(data, jsonAssetPath, uiName);
        }

        #endregion

        #region 菜单入口

        [MenuItem("Assets/PS2UGUI/生成 Prefab", false, 0)]
        private static void GeneratePrefabMenuItem()
        {
            ExecuteGeneration();
        }

        [MenuItem("Assets/PS2UGUI/生成 Prefab", true)]
        private static bool GeneratePrefabValidation()
        {
            return ValidateSelection();
        }

        private static bool ValidateSelection()
        {
            var obj = Selection.activeObject;
            if (obj == null) return false;
            var path = AssetDatabase.GetAssetPath(obj);
            return IsSupportedPrefabJsonPath(path);
        }

        private static void ExecuteGeneration()
        {
            var selected = Selection.activeObject;
            var path = AssetDatabase.GetAssetPath(selected);

            UnityData data = PS2UGUITranslate.Translate(path);

            if (data == null || data.canvas == null || data.children == null)
            {
                EditorUtility.DisplayDialog("PS2UGUI", "数据转换失败。", "确定");
                return;
            }

            var uiName = GetPrefabNameFromJsonPath(path);
            GeneratePrefab(data, path, uiName);
        }

        internal static bool IsSupportedPrefabJsonPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (var suffix in SupportedJsonFileSuffixes)
            {
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static string GetPrefabNameFromJsonPath(string jsonPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(jsonPath);
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            foreach (var suffix in PrefabNameSuffixes)
            {
                if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var prefabName = fileName.Substring(0, fileName.Length - suffix.Length);
                return string.IsNullOrEmpty(prefabName) ? fileName : prefabName;
            }

            return fileName;
        }

        #endregion

        #region Prefab 生成

        /// <summary>
        /// 生成 Prefab 到磁盘。
        /// </summary>
        internal static void GeneratePrefab(UnityData data, string jsonPath, string uiName)
        {
            var dir = Path.GetDirectoryName(jsonPath)?.Replace("\\", "/");
            _currentJsonDirectory = string.IsNullOrEmpty(dir) ? "Assets/" : dir + "/";

            var prefabPath = ResolvePrefabOutputPath(jsonPath, uiName);
            if (string.IsNullOrEmpty(prefabPath))
            {
                EditorUtility.DisplayDialog("PS2UGUI",
                    $"无法确定 Prefab 输出路径。\n\nJSON 位置: {jsonPath}\n请确保 JSON 位于 Art/Textures/ 目录下。", "确定");
                return;
            }

            if (AssetDatabase.GetMainAssetTypeAtPath(prefabPath) != null)
            {
                if (!EditorUtility.DisplayDialog("PS2UGUI",
                        $"目标 Prefab 已存在：\n{prefabPath}\n\n是否覆盖？", "覆盖", "取消"))
                {
                    return;
                }

                AssetDatabase.DeleteAsset(prefabPath);
            }

            // 确保输出目录存在
            var prefabDir = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(prefabDir) && !AssetDatabase.IsValidFolder(prefabDir))
            {
                CreateFolderRecursive(prefabDir);
            }

            // 创建 prefab 根节点
            var prefabRoot = CreatePrefabRoot(uiName);

            if (data.children != null)
            {
                foreach (var child in data.children)
                    CreateNode(child, prefabRoot.transform);
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            GameObject.DestroyImmediate(prefabRoot);

            AssetDatabase.Refresh();

            var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = newPrefab;
            EditorGUIUtility.PingObject(newPrefab);
            AssetDatabase.OpenAsset(newPrefab);

            Debug.Log($"PS2UGUI: 已生成 Prefab → {prefabPath}");
            _currentJsonDirectory = null;
        }

        private static GameObject CreatePrefabRoot(string name)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            return root;
        }

        #endregion

        #region 路径解析

        /// <summary>
        /// 从 JSON 路径解析 prefab 输出路径。
        /// 从 JSON 所在目录向上逐级查找包含 Res/ 子目录的那一层，
        /// 输出到 {找到的层}/Res/Prefabs/UI/{name}/{name}.prefab
        /// </summary>
        private static string ResolvePrefabOutputPath(string jsonPath, string prefabName)
        {
            var normalized = jsonPath.Replace("\\", "/");
            var dir = Path.GetDirectoryName(normalized)?.Replace("\\", "/");

            while (!string.IsNullOrEmpty(dir) && dir.Contains("/"))
            {
                var resCandidate = dir + "/Res";
                if (AssetDatabase.IsValidFolder(resCandidate))
                    return $"{dir}/Res/Prefabs/UI/{prefabName}/{prefabName}.prefab";

                var lastSlash = dir.LastIndexOf('/');
                dir = lastSlash >= 0 ? dir.Substring(0, lastSlash) : "";
            }

            return null;
        }

        /// <summary>
        /// 解析 sprite 完整路径（混合模式）。
        /// 无 /：本地 sprite，在 JSON 同目录查找。
        /// 有 /：跨目录 sprite，从 JSON 目录向上递归查找第一段目录名。
        /// </summary>
        private static string ResolveSpriteFullPath(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            var jsonDir = _currentJsonDirectory ?? "Assets/";

            if (!spriteName.Contains("/"))
            {
                return jsonDir + spriteName + ".png";
            }

            var firstSlash = spriteName.IndexOf('/');
            var firstSegment = spriteName.Substring(0, firstSlash);
            var dir = jsonDir.TrimEnd('/');

            while (!string.IsNullOrEmpty(dir) && dir.Contains("/"))
            {
                var candidate = dir + "/" + firstSegment;
                if (AssetDatabase.IsValidFolder(candidate))
                    return dir + "/" + spriteName + ".png";
                var lastSlash = dir.LastIndexOf('/');
                dir = lastSlash >= 0 ? dir.Substring(0, lastSlash) : "";
            }

            Debug.LogWarning($"PS2UGUI: 向上递归查找失败 → {spriteName}（从 {jsonDir} 开始）");
            return jsonDir + spriteName + ".png";
        }

        /// <summary>
        /// 递归创建 Asset 文件夹
        /// </summary>
        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                CreateFolderRecursive(parent);
            }

            var folderName = Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        #endregion

        #region 节点创建

        private static void CreateNode(UnityNode nodeData, Transform parent)
        {
            GameObject go;

            switch (nodeData.type)
            {
                case "button":
                    go = CreateButtonNode(nodeData);
                    break;
                case "text":
                    go = CreateTextNode(nodeData);
                    break;
                case "slider":
                    go = CreateSliderNode(nodeData);
                    break;
                case "image":
                    go = CreateImageNode(nodeData);
                    break;
                case "prefab":
                    go = CreatePrefabNode(nodeData, parent.gameObject.scene);
                    break;
                case "node":
                default:
                    go = CreateEmptyNode(nodeData);
                    break;
            }

            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            if (rect == null)
                rect = go.AddComponent<RectTransform>();

            ApplyAnchorPreset(rect, nodeData);
            ApplyRotationZ(rect, nodeData, parent);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (nodeData.type == "text" && tmp != null)
                ApplyDesiredVisualFontSize(tmp, nodeData, parent);
            else
                ApplyDesiredVisualScale(rect, nodeData, parent);

            if (nodeData.useNativeSize)
            {
                var image = go.GetComponent<Image>();
                if (image != null && image.sprite != null)
                    image.SetNativeSize();
            }

            go.layer = LayerMask.NameToLayer("UI");

            if (!nodeData.active)
                go.SetActive(false);

            if (nodeData.children != null)
            {
                foreach (var child in nodeData.children)
                    CreateNode(child, go.transform);
            }
        }

        private static GameObject CreateEmptyNode(UnityNode nodeData)
        {
            return new GameObject(nodeData.name);
        }

        private static GameObject CreateImageNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var image = go.AddComponent<Image>();
            image.type = nodeData.imageType == "tiled" ? Image.Type.Tiled : Image.Type.Simple;
            image.raycastTarget = nodeData.raycastTarget;

            LoadSprite(image, nodeData.spritePath);

            if (nodeData.imageType == "sliced")
                ApplySlicedType(image, nodeData.spritePath);

            ApplyColorAndOpacity(image, nodeData.colorHex, nodeData.opacity);

            if (nodeData.addUIButton)
            {
                go.AddComponent<UISmartButton>();
            }

            return go;
        }

        private static GameObject CreatePrefabNode(UnityNode nodeData, Scene destinationScene)
        {
            if (string.IsNullOrEmpty(nodeData.prefabPath))
                return new GameObject(nodeData.name);

            var fullPath = ResolveReferencedPrefabPath(nodeData.prefabPath);
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogWarning($"PS2UGUI: 外部 Prefab 路径解析失败 → {nodeData.prefabPath}");
                return new GameObject(nodeData.name);
            }

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (prefabAsset == null)
            {
                Debug.LogWarning($"PS2UGUI: 外部 Prefab 加载失败 → {fullPath}");
                return new GameObject(nodeData.name);
            }

            var prefabInstance = PrefabUtility.InstantiatePrefab(prefabAsset, destinationScene) as GameObject;
            if (prefabInstance == null)
            {
                Debug.LogWarning($"PS2UGUI: 外部 Prefab 实例化失败 → {fullPath}");
                return new GameObject(nodeData.name);
            }

            prefabInstance.name = nodeData.name;
            return prefabInstance;
        }

        /// <summary>
        /// 从 _currentJsonDirectory 向上查找 Res/ 目录，拼接 Res/Prefabs/UI/{prefabPath}.prefab
        /// </summary>
        private static string ResolveReferencedPrefabPath(string prefabPath)
        {
            var dir = (_currentJsonDirectory ?? "Assets/").TrimEnd('/');

            while (!string.IsNullOrEmpty(dir) && dir.Contains("/"))
            {
                var resCandidate = dir + "/Res";
                if (AssetDatabase.IsValidFolder(resCandidate))
                    return $"{dir}/Res/Prefabs/UI/{prefabPath}.prefab";

                var lastSlash = dir.LastIndexOf('/');
                dir = lastSlash >= 0 ? dir.Substring(0, lastSlash) : "";
            }

            return null;
        }

        private static GameObject CreateButtonNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.raycastTarget = true;

            LoadSprite(image, nodeData.spritePath);

            if (nodeData.imageType == "sliced")
                ApplySlicedType(image, nodeData.spritePath);

            ApplyColorAndOpacity(image, nodeData.colorHex, nodeData.opacity);

            go.AddComponent<UISmartButton>();

            return go;
        }

        private static GameObject CreateTextNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var tmp = go.AddComponent<TextMeshProUGUI>();

            ApplyDefaultFont(tmp);

            tmp.text = nodeData.text;
            tmp.fontSize = TruncateToTwoDecimals(nodeData.fontSize);
            tmp.raycastTarget = false;

            if (!string.IsNullOrEmpty(nodeData.colorHex))
            {
                if (ColorUtility.TryParseHtmlString(nodeData.colorHex, out Color color))
                {
                    int opacity = MapOpacity(nodeData.opacity);
                    if (opacity < 100)
                        color.a = opacity / 100f;
                    tmp.color = color;
                }
            }
            else if (nodeData.opacity < 100)
            {
                int opacity = MapOpacity(nodeData.opacity);
                var c = tmp.color;
                c.a = opacity / 100f;
                tmp.color = c;
            }

            tmp.alignment = MapAlignment(nodeData.alignment);

            if (!string.IsNullOrEmpty(nodeData.strokeColor) && nodeData.strokeWidth > 0)
            {
                var colorHex = nodeData.strokeColor.TrimStart('#').ToUpper();
                var widthStr = FormatStrokeWidth(nodeData.strokeWidth);
                var matFileName = $"{FontMaterialPrefix}{colorHex} {widthStr}.mat";
                var matPath = FontMaterialPath + matFileName;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    mat = CreateOutlineMaterial(matPath, tmp.font, nodeData.strokeColor, nodeData.strokeWidth);
                if (mat != null)
                    tmp.fontSharedMaterial = mat;
            }

            return go;
        }

        private static GameObject CreateSliderNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.raycastTarget = true;

            LoadSprite(image, nodeData.spritePath);
            ApplyColorAndOpacity(image, nodeData.colorHex, nodeData.opacity);

            var slider = go.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            if (!string.IsNullOrEmpty(nodeData.fillSpritePath))
            {
                var fillGo = new GameObject("Fill");
                fillGo.transform.SetParent(go.transform, false);
                fillGo.layer = LayerMask.NameToLayer("UI");

                var fillRect = fillGo.AddComponent<RectTransform>();
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;

                var fillImage = fillGo.AddComponent<Image>();
                fillImage.type = Image.Type.Filled;
                fillImage.raycastTarget = false;

                if (nodeData.fillDirection == "vertical")
                {
                    fillImage.fillMethod = Image.FillMethod.Vertical;
                    fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                }
                else
                {
                    fillImage.fillMethod = Image.FillMethod.Horizontal;
                    fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }

                LoadSprite(fillImage, nodeData.fillSpritePath);

                slider.fillRect = fillRect;
            }

            return go;
        }

        #endregion

        #region 工具方法

        private static void ApplyDefaultFont(TextMeshProUGUI tmp)
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontAssetPath);
            if (fontAsset != null)
                tmp.font = fontAsset;
        }

        private static void ApplyAnchorPreset(RectTransform rect, UnityNode nodeData)
        {
            if (nodeData.anchorPreset == "stretch-all")
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = TruncateToTwoDecimals(new Vector2(nodeData.x, nodeData.y));
                rect.sizeDelta = TruncateToTwoDecimals(new Vector2(nodeData.width, nodeData.height));
            }
        }

        private static void ApplyRotationZ(RectTransform rect, UnityNode nodeData, Transform parent)
        {
            var eulerAngles = rect.localEulerAngles;
            var parentRotationZ = GetAccumulatedParentRotationZ(parent);
            var localRotationZ = ResolveCompensatedLocalRotation(nodeData.rotationZ, parentRotationZ);
            rect.localRotation = Quaternion.Euler(eulerAngles.x, eulerAngles.y, localRotationZ);
        }

        private static void ApplyDesiredVisualScale(RectTransform rect, UnityNode nodeData, Transform parent)
        {
            var parentScale = GetAccumulatedParentScale(parent);
            var baseScale = rect.localScale;

            rect.localScale = TruncateToTwoDecimals(new Vector3(
                ResolveCompensatedLocalScale(baseScale.x, nodeData.scaleX, parentScale.x),
                ResolveCompensatedLocalScale(baseScale.y, nodeData.scaleY, parentScale.y),
                baseScale.z));
        }

        private static void ApplyDesiredVisualFontSize(TextMeshProUGUI tmp, UnityNode nodeData, Transform parent)
        {
            var parentScale = GetAccumulatedParentScale(parent);

            if (Mathf.Abs(nodeData.scaleX - nodeData.scaleY) > ScaleCompensationEpsilon ||
                Mathf.Abs(parentScale.x - parentScale.y) > ScaleCompensationEpsilon)
            {
                Debug.LogWarning(
                    $"PS2UGUI: 文本节点 {nodeData.name} 检测到非等比缩放，将按 Y 轴进行字号补偿。" +
                    $" nodeScale=({nodeData.scaleX:F4}, {nodeData.scaleY:F4})," +
                    $" parentScale=({parentScale.x:F4}, {parentScale.y:F4})");
            }

            tmp.fontSize = TruncateToTwoDecimals(ResolveCompensatedFontSize(tmp.fontSize, nodeData.scaleY, parentScale.y));
        }

        private static Vector3 GetAccumulatedParentScale(Transform parent)
        {
            var accumulatedScale = Vector3.one;
            var current = parent;

            while (current != null)
            {
                accumulatedScale = Vector3.Scale(accumulatedScale, current.localScale);
                current = current.parent;
            }

            return accumulatedScale;
        }

        private static float GetAccumulatedParentRotationZ(Transform parent)
        {
            var accumulatedRotation = 0f;
            var current = parent;

            while (current != null)
            {
                accumulatedRotation = NormalizeAngle(accumulatedRotation + NormalizeAngle(current.localEulerAngles.z));
                current = current.parent;
            }

            return accumulatedRotation;
        }

        private static float ResolveCompensatedLocalScale(float baseScale, float desiredVisualScale, float parentAccumulatedScale)
        {
            if (Mathf.Abs(parentAccumulatedScale) <= ScaleCompensationEpsilon)
                return baseScale * desiredVisualScale;

            return baseScale * desiredVisualScale / parentAccumulatedScale;
        }

        private static float ResolveCompensatedLocalRotation(float desiredVisualRotation, float parentAccumulatedRotation)
        {
            return NormalizeAngle(desiredVisualRotation - parentAccumulatedRotation);
        }

        private static float ResolveCompensatedFontSize(float baseFontSize, float desiredVisualScaleY, float parentAccumulatedScaleY)
        {
            var safeDesiredScaleY = Mathf.Abs(desiredVisualScaleY);
            var safeParentAccumulatedScaleY = Mathf.Abs(parentAccumulatedScaleY);

            if (safeParentAccumulatedScaleY <= ScaleCompensationEpsilon)
                return baseFontSize * safeDesiredScaleY;

            return baseFontSize * safeDesiredScaleY / safeParentAccumulatedScaleY;
        }

        private static Vector2 TruncateToTwoDecimals(Vector2 value)
        {
            return new Vector2(TruncateToTwoDecimals(value.x), TruncateToTwoDecimals(value.y));
        }

        private static Vector3 TruncateToTwoDecimals(Vector3 value)
        {
            return new Vector3(TruncateToTwoDecimals(value.x), TruncateToTwoDecimals(value.y), TruncateToTwoDecimals(value.z));
        }

        private static float TruncateToTwoDecimals(float value)
        {
            double adjustedValue = value;
            if (Mathf.Abs(value) > 0f)
                adjustedValue += Math.Sign(value) * SerializedValueTruncateEpsilon;

            return (float)(Math.Truncate(adjustedValue * 100d) / 100d);
        }

        private static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        private static void LoadSprite(Image image, string spritePath)
        {
            if (string.IsNullOrEmpty(spritePath)) return;

            var fullPath = ResolveSpriteFullPath(spritePath);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);

            if (sprite != null)
            {
                image.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"PS2UGUI: Sprite 加载失败 → {fullPath}");
            }
        }

        private static void ApplyColorAndOpacity(Image image, string colorHex, int rawOpacity)
        {
            int opacity = MapOpacity(rawOpacity);

            if (!string.IsNullOrEmpty(colorHex))
            {
                if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
                {
                    if (opacity < 100)
                        color.a = opacity / 100f;
                    image.color = color;
                    return;
                }
            }

            if (opacity < 100)
            {
                var color = image.color;
                color.a = opacity / 100f;
                image.color = color;
            }
        }

        private static TextAlignmentOptions MapAlignment(string alignment)
        {
            switch (alignment)
            {
                case "left-top": return TextAlignmentOptions.TopLeft;
                case "center-top": return TextAlignmentOptions.Top;
                case "right-top": return TextAlignmentOptions.TopRight;
                case "left-middle": return TextAlignmentOptions.Left;
                case "center-middle": return TextAlignmentOptions.Center;
                case "right-middle": return TextAlignmentOptions.Right;
                case "left-bottom": return TextAlignmentOptions.BottomLeft;
                case "center-bottom": return TextAlignmentOptions.Bottom;
                case "right-bottom": return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        private static Material CreateOutlineMaterial(string matPath, TMP_FontAsset font, string strokeColorHex, float strokeWidth)
        {
            if (font == null || font.material == null) return null;

            var mat = new Material(font.material);
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetFloat("_UnderlayOffsetX", 0);
            mat.SetFloat("_UnderlayOffsetY", 0);
            mat.SetFloat("_UnderlaySoftness", 0);
            mat.SetFloat("_UnderlayDilate", strokeWidth * 0.1f);

            if (ColorUtility.TryParseHtmlString(strokeColorHex, out Color color))
                mat.SetColor("_UnderlayColor", new Color(color.r, color.g, color.b, 1f));

            var dir = Path.GetDirectoryName(matPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"PS2UGUI: 自动创建描边 Material (Underlay) → {matPath}");
            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        private static string FormatStrokeWidth(float width)
        {
            int intVal = (int)width;
            if (Mathf.Approximately(width, intVal))
                return intVal.ToString("D2");
            var s = width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return s.Replace(".", "").PadLeft(2, '0');
        }

        private static int MapOpacity(int psOpacity)
        {
            int rounded = (int)(Math.Round(psOpacity / 5.0) * 5);
            rounded = Math.Max(0, Math.Min(100, rounded));

            if (OpacityMap.TryGetValue(rounded, out int mapped) && mapped >= 0)
                return mapped;

            return psOpacity;
        }

        #endregion

        #region 九宫格检测

        private struct SliceBorderResult
        {
            public bool success;
            public int left, right, top, bottom;
        }

        private static SliceBorderResult DetectSliceBorder(Texture2D texture)
        {
            var result = new SliceBorderResult();

            int w = texture.width;
            int h = texture.height;
            if (w <= 2 || h <= 2)
                return result;

            var pixels = texture.GetPixels32();

            bool lrFound = TryDetectAxisBorder(
                w,
                (index, referenceIndex) => ColumnEquals(pixels, index, referenceIndex, w, h),
                out int left, out int right);
            if (lrFound)
            {
                result.left = left;
                result.right = right;
            }

            bool tbFound = TryDetectAxisBorder(
                h,
                (index, referenceIndex) => RowEquals(pixels, index, referenceIndex, w),
                out int bottom, out int top);
            if (tbFound)
            {
                result.top = top;
                result.bottom = bottom;
            }

            result.success = lrFound || tbFound;
            return result;
        }

        private static bool TryDetectAxisBorder(int size, Func<int, int, bool> lineEquals, out int startBorder, out int endBorder)
        {
            startBorder = -1;
            endBorder = -1;

            int centerStart = (size - 1) / 2;
            int centerEnd = size / 2;
            int minIndex = centerStart - (SliceMinimumCenterPixelsPerSide - 1);
            int maxIndex = centerEnd + (SliceMinimumCenterPixelsPerSide - 1);

            if (minIndex <= 0 || maxIndex >= size - 1)
                return false;

            for (int offset = 0; offset < SliceMinimumCenterPixelsPerSide; offset++)
            {
                if (!lineEquals(centerStart - offset, centerStart))
                    return false;

                if (!lineEquals(centerEnd + offset, centerEnd))
                    return false;
            }

            startBorder = minIndex;
            endBorder = size - 1 - maxIndex;
            return startBorder > 0 && startBorder < size / 2 && endBorder > 0 && endBorder < size / 2;
        }

        private static bool ColumnEquals(Color32[] pixels, int colA, int colB, int w, int h)
        {
            for (int row = 0; row < h; row++)
            {
                if (!SlicePixelEquals(pixels[row * w + colA], pixels[row * w + colB]))
                    return false;
            }
            return true;
        }

        private static bool RowEquals(Color32[] pixels, int rowA, int rowB, int w)
        {
            int offsetA = rowA * w;
            int offsetB = rowB * w;
            for (int col = 0; col < w; col++)
            {
                if (!SlicePixelEquals(pixels[offsetA + col], pixels[offsetB + col]))
                    return false;
            }
            return true;
        }

        private static bool SlicePixelEquals(Color32 a, Color32 b)
        {
            bool aVisible = a.a > 0;
            bool bVisible = b.a > 0;
            if (aVisible != bVisible) return false;
            if (!aVisible) return true;

            return ChannelEqualsWithinTolerance(a.r, b.r)
                && ChannelEqualsWithinTolerance(a.g, b.g)
                && ChannelEqualsWithinTolerance(a.b, b.b)
                && ChannelEqualsWithinTolerance(a.a, b.a);
        }

        private static bool ChannelEqualsWithinTolerance(byte a, byte b)
        {
            return Math.Abs(a - b) <= SlicePixelTolerance;
        }


        private static void ApplySlicedType(Image image, string spritePath)
        {
            var fullPath = ResolveSpriteFullPath(spritePath);
            var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"PS2UGUI: 九宫格处理失败，TextureImporter 不存在 → {fullPath}");
                return;
            }

            Vector4 existingBorder = importer.spriteBorder;
            if (existingBorder.x > 0 || existingBorder.y > 0 || existingBorder.z > 0 || existingBorder.w > 0)
            {
                image.type = Image.Type.Sliced;
                return;
            }

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            if (texture == null)
            {
                Debug.LogWarning($"PS2UGUI: 九宫格检测失败，纹理加载失败 → {fullPath}");
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
                return;
            }

            var borderResult = DetectSliceBorder(texture);

            if (borderResult.success)
            {
                if (!wasReadable)
                    importer.isReadable = false;

                importer.spriteBorder = new Vector4(
                    borderResult.left, borderResult.bottom,
                    borderResult.right, borderResult.top);
                importer.SaveAndReimport();

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
                if (sprite != null)
                    image.sprite = sprite;

                image.type = Image.Type.Sliced;
            }
            else
            {
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }

                Debug.LogWarning($"PS2UGUI: 九宫格 border 检测失败 → {fullPath}");
            }
        }

        #endregion
    }
}
