using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace July.UI.Authoring.Editor
{
    /// <summary>
    /// 将 PS/Figma 导出的布局 JSON 转换为 UnityData 对象
    /// </summary>
    public static class PS2UGUITranslate
    {
        private const float ContainmentThreshold = 0.55f;
        private const float ScaleEpsilon = 0.0001f;

        #region 输入数据模型

        [Serializable]
        private class PSData
        {
            public PSCanvas canvas;
            public List<PSLayer> layers;
        }

        [Serializable]
        private class PSCanvas
        {
            public int width;
            public int height;
        }

        [Serializable]
        private class PSLayer
        {
            public int id;
            public string name;
            public string groupPath;
            public string type;
            public int x;
            public int y;
            public int width;
            public int height;
            public bool visible;
            public int order;
            public int opacity;
            public string text;
            public float fontSize;
            public string fontColor;
            public string fontName;
            public string textAlignment;
            public float scaleX;
            public float scaleY;
            public float rotationZ;
            public string strokeColor;
            public float strokeWidth;
        }

        #endregion

        #region 内部工作结构

        private class LayerInfo
        {
            public PSLayer Source;
            public string Prefix;
            public string ResourceName;
            public string SpritePath;
            public string PrefabPath;
            public string UnityName;
            public string UnityType;
            public bool IsSliderfill;
            public string FillSpritePath;

            public LayerInfo Parent;
            public List<LayerInfo> Children = new List<LayerInfo>();
            public bool Claimed;
            public bool UseNativeSize;
            public bool IsSliced;
            public float NameScale = 1f;

            public float AbsUnityX;
            public float AbsUnityY;
            public float AbsRotationZ;

            public int PsArea => Source.width * Source.height;
        }

        #endregion

        #region 公开接口

        internal static UnityData Translate(string psDataJsonPath)
        {
            var jsonText = File.ReadAllText(psDataJsonPath);
            var psData = LitJson.JsonMapper.ToObject<PSData>(jsonText);

            if (psData?.layers != null)
            {
                foreach (var layer in psData.layers)
                {
                    if (layer.scaleX == 0f) layer.scaleX = 1f;
                    if (layer.scaleY == 0f) layer.scaleY = 1f;
                }
            }

            if (psData == null || psData.canvas == null || psData.layers == null)
            {
                EditorUtility.DisplayDialog("PS2UGUI", "JSON 解析失败，请检查文件格式。", "确定");
                return null;
            }

            if (psData.canvas.width <= 0 || psData.canvas.height <= 0)
            {
                EditorUtility.DisplayDialog("PS2UGUI", "画布尺寸无效。", "确定");
                return null;
            }

            return Translate(psData);
        }

        #endregion

        #region 核心转换流程

        private static UnityData Translate(PSData psData)
        {
            int canvasW = psData.canvas.width;
            int canvasH = psData.canvas.height;

            var layers = ClassifyLayers(psData.layers);
            PairSliders(layers);
            var activeLayers = FilterActiveLayers(layers);
            GroupButtonChildren(activeLayers);
            AssignUnityNames(layers);
            InferHierarchy(activeLayers);
            ConvertCoordinates(activeLayers, canvasW, canvasH);

            // bg/mask/tile 排前面（先渲染在底层），其余按 order 排序
            var bgNodes = new List<UnityNode>();
            var contentNodes = new List<UnityNode>();

            foreach (var layer in activeLayers)
            {
                if (layer.Parent != null) continue;

                var node = BuildUnityNode(layer);
                if (layer.Prefix == "bg" || layer.Prefix == "mask" || layer.Prefix == "tile")
                    bgNodes.Add(node);
                else
                    contentNodes.Add(node);
            }

            bgNodes.Sort((a, b) => b.order.CompareTo(a.order));
            contentNodes.Sort((a, b) => b.order.CompareTo(a.order));

            var children = new List<UnityNode>(bgNodes.Count + contentNodes.Count);
            children.AddRange(bgNodes);
            children.AddRange(contentNodes);

            return new UnityData
            {
                canvas = new UnityCanvas { width = canvasW, height = canvasH },
                children = children
            };
        }

        #endregion

        #region Step 1: 解析 & 分类图层

        private static List<LayerInfo> ClassifyLayers(List<PSLayer> psLayers)
        {
            var result = new List<LayerInfo>();

            foreach (var layer in psLayers)
            {
                var fullName = layer.name ?? "";
                if (fullName.StartsWith("#")) continue;

                var groupPath = layer.groupPath ?? "";
                if (IsGroupPathIgnored(groupPath)) continue;

                var isTextLayer = layer.type == "text";
                fullName = ExtractNameScale(fullName, isTextLayer, out var nameScale);
                var info = new LayerInfo { Source = layer, NameScale = isTextLayer ? 1f : nameScale };

                if (isTextLayer)
                {
                    info.Prefix = "text";
                    info.ResourceName = fullName;
                    info.SpritePath = "";
                    info.UnityType = "text";
                }
                else
                {
                    // Strip tint suffix ~ (color already captured in fontColor by Figma plugin)
                    if (fullName.EndsWith("~"))
                        fullName = fullName.Substring(0, fullName.Length - 1);

                    bool isPrefab = fullName.EndsWith("@");
                    if (isPrefab)
                    {
                        fullName = fullName.Substring(0, fullName.Length - 1).Replace('_', '/').Trim('/');
                    }

                    bool useNativeSize = !isPrefab && fullName.EndsWith("$");
                    if (useNativeSize)
                        fullName = fullName.Substring(0, fullName.Length - 1);

                    bool isSliced = !isPrefab && fullName.EndsWith("%");
                    if (isSliced)
                        fullName = fullName.Substring(0, fullName.Length - 1);

                    if (isPrefab && (fullName.EndsWith("$") || fullName.EndsWith("%")))
                    {
                        Debug.LogWarning($"PS2UGUI: 图层 '{layer.name}' 的 @ 命名不支持 $ 或 % 后缀，忽略该图层。");
                        continue;
                    }

                    if (useNativeSize && isSliced)
                    {
                        Debug.LogWarning($"PS2UGUI: 图层 '{layer.name}' 同时含 $ 和 % 后缀，$ 和 % 不可同时使用，% 将被忽略。");
                        isSliced = false;
                    }

                    var lastSlash = fullName.LastIndexOf('/');
                    var resourcePart = lastSlash >= 0 ? fullName.Substring(lastSlash + 1) : fullName;
                    if (string.IsNullOrEmpty(resourcePart))
                    {
                        Debug.LogWarning($"PS2UGUI: 图层 '{layer.name}' 缺少有效资源名，忽略该图层。");
                        continue;
                    }

                    if (isPrefab)
                    {
                        info.Prefix = "prefab";
                        info.ResourceName = resourcePart;
                        info.PrefabPath = fullName;
                        info.SpritePath = "";
                        info.UnityType = "prefab";
                        result.Add(info);
                        continue;
                    }

                    info.SpritePath = fullName;

                    if (resourcePart.StartsWith("btn_"))
                    {
                        info.Prefix = "btn";
                        info.ResourceName = resourcePart.Substring(4);
                        info.UnityType = "button";
                    }
                    else if (resourcePart.StartsWith("sliderfill_"))
                    {
                        info.Prefix = "sliderfill";
                        info.ResourceName = resourcePart.Substring(11);
                        info.UnityType = "";
                        info.IsSliderfill = true;
                    }
                    else if (resourcePart.StartsWith("slider_"))
                    {
                        info.Prefix = "slider";
                        info.ResourceName = resourcePart.Substring(7);
                        info.UnityType = "slider";
                    }
                    else if (resourcePart.StartsWith("bg_"))
                    {
                        info.Prefix = "bg";
                        info.ResourceName = resourcePart.Substring(3);
                        info.UnityType = "image";
                    }
                    else if (resourcePart.StartsWith("mask_"))
                    {
                        info.Prefix = "mask";
                        info.ResourceName = resourcePart.Substring(5);
                        info.UnityType = "image";
                    }
                    else if (resourcePart.StartsWith("tile_"))
                    {
                        info.Prefix = "tile";
                        info.ResourceName = resourcePart.Substring(5);
                        info.UnityType = "image";
                    }
                    else if (resourcePart.StartsWith("img_"))
                    {
                        info.Prefix = "img";
                        info.ResourceName = resourcePart.Substring(4);
                        info.UnityType = "image";
                    }
                    else if (resourcePart.StartsWith("icon_"))
                    {
                        info.Prefix = "icon";
                        info.ResourceName = resourcePart.Substring(5);
                        info.UnityType = "image";
                    }
                    else
                    {
                        Debug.LogWarning($"PS2UGUI: 未识别的图层前缀 '{resourcePart}'，忽略该图层。groupPath={layer.groupPath}");
                        continue;
                    }

                    info.UseNativeSize = useNativeSize;
                    info.IsSliced = isSliced;
                }

                result.Add(info);
            }

            return result;
        }

        #endregion

        #region Step 2: Slider 配对

        private static void PairSliders(List<LayerInfo> layers)
        {
            var fillDict = new Dictionary<string, LayerInfo>();
            foreach (var layer in layers)
            {
                if (layer.IsSliderfill)
                    fillDict[layer.ResourceName] = layer;
            }

            foreach (var layer in layers)
            {
                if (layer.Prefix == "slider" && fillDict.TryGetValue(layer.ResourceName, out var fill))
                {
                    layer.FillSpritePath = fill.SpritePath;
                    fill.Claimed = true;
                }
                else if (layer.Prefix == "slider")
                {
                    Debug.LogWarning($"PS2UGUI: slider_{layer.ResourceName} 未找到对应的 sliderfill，fillSpritePath 为空。");
                }
            }
        }

        #endregion

        #region Step 3: 生成 Unity 节点名

        private static void AssignUnityNames(List<LayerInfo> layers)
        {
            var usedNames = new HashSet<string>();

            foreach (var layer in layers)
            {
                if (layer.IsSliderfill && layer.Claimed) continue;
                if (layer.Prefix == "text") continue;

                string baseName;
                switch (layer.Prefix)
                {
                    case "btn":
                        baseName = ToPascalCase(layer.ResourceName) + "Btn";
                        break;
                    case "bg":
                        baseName = ToPascalCase(layer.ResourceName) + "Bg";
                        break;
                    case "mask":
                        var maskBody = ToPascalCase(layer.ResourceName);
                        baseName = string.IsNullOrEmpty(maskBody) ? "Mask" : maskBody + "Mask";
                        break;
                    case "tile":
                        baseName = ToPascalCase(layer.ResourceName) + "Tile";
                        break;
                    case "img":
                        baseName = ToPascalCase(layer.ResourceName) + "Img";
                        break;
                    case "icon":
                        baseName = ToPascalCase(layer.ResourceName) + "Icon";
                        break;
                    case "slider":
                        baseName = ToPascalCase(layer.ResourceName) + "Slider";
                        break;
                    case "prefab":
                        baseName = layer.ResourceName;
                        break;
                    default:
                        baseName = ToPascalCase(layer.ResourceName) + "Node";
                        break;
                }

                string finalName = baseName;
                int suffix = 1;
                while (usedNames.Contains(finalName))
                {
                    finalName = baseName + suffix;
                    suffix++;
                }

                usedNames.Add(finalName);
                layer.UnityName = finalName;
            }

            foreach (var layer in layers)
            {
                if (layer.Prefix != "text") continue;

                string baseName;
                if (layer.Parent != null && layer.Parent.Prefix == "btn")
                    baseName = layer.Parent.UnityName + "Tx";
                else
                    baseName = "Tx";

                string finalName = baseName;
                int suffix = 1;
                while (usedNames.Contains(finalName))
                {
                    finalName = baseName + suffix;
                    suffix++;
                }

                usedNames.Add(finalName);
                layer.UnityName = finalName;
            }
        }

        #endregion

        #region Step 4: 过滤活跃图层

        private static List<LayerInfo> FilterActiveLayers(List<LayerInfo> layers)
        {
            var result = new List<LayerInfo>();
            foreach (var layer in layers)
            {
                if (layer.IsSliderfill && layer.Claimed) continue;
                result.Add(layer);
            }

            return result;
        }

        #endregion

        #region Step 5: 层级推断

        private static void InferHierarchy(List<LayerInfo> rootLayers)
        {
            AssignSpatialParents(rootLayers);

            foreach (var layer in rootLayers)
            {
                if (layer.Children.Count > 0)
                    layer.Children.Sort((a, b) => b.Source.order.CompareTo(a.Source.order));
            }
        }

        private static void GroupButtonChildren(List<LayerInfo> rootLayers)
        {
            var buttons = new List<LayerInfo>();
            foreach (var layer in rootLayers)
            {
                if (layer.Prefix == "btn")
                    buttons.Add(layer);
            }

            foreach (var layer in rootLayers)
            {
                if (layer.Prefix != "text" && layer.Prefix != "icon") continue;

                float cx = layer.Source.x + layer.Source.width / 2f;
                float cy = layer.Source.y + layer.Source.height / 2f;

                LayerInfo bestBtn = null;
                int bestArea = int.MaxValue;

                foreach (var btn in buttons)
                {
                    int bx = btn.Source.x;
                    int by = btn.Source.y;
                    int bw = btn.Source.width;
                    int bh = btn.Source.height;

                    if (cx >= bx && cx <= bx + bw && cy >= by && cy <= by + bh)
                    {
                        int area = btn.PsArea;
                        if (area < bestArea)
                        {
                            bestArea = area;
                            bestBtn = btn;
                        }
                    }
                }

                if (bestBtn != null)
                {
                    layer.Claimed = true;
                    layer.Parent = bestBtn;
                    bestBtn.Children.Add(layer);
                }
            }
        }

        private static void AssignSpatialParents(List<LayerInfo> rootLayers)
        {
            foreach (var layer in rootLayers)
            {
                if (layer.Claimed) continue;
                if (layer.IsSliderfill) continue;

                LayerInfo bestParent = null;
                int bestArea = int.MaxValue;

                foreach (var candidate in rootLayers)
                {
                    if (candidate == layer) continue;
                    if (candidate.Claimed) continue;
                    if (candidate.Prefix == "text") continue;
                    if (candidate.IsSliderfill) continue;
                    if (candidate.Source.order <= layer.Source.order) continue;

                    float overlapRatio = ComputeOverlapRatio(layer.Source, candidate.Source);
                    if (overlapRatio > ContainmentThreshold)
                    {
                        int area = candidate.PsArea;
                        if (area < bestArea)
                        {
                            bestArea = area;
                            bestParent = candidate;
                        }
                    }
                }

                if (bestParent != null)
                {
                    layer.Parent = bestParent;
                    bestParent.Children.Add(layer);
                }
            }
        }

        private static float ComputeOverlapRatio(PSLayer child, PSLayer parent)
        {
            int childArea = child.width * child.height;
            if (childArea <= 0) return 0;

            int overlapX = Math.Max(0, Math.Min(child.x + child.width, parent.x + parent.width) - Math.Max(child.x, parent.x));
            int overlapY = Math.Max(0, Math.Min(child.y + child.height, parent.y + parent.height) - Math.Max(child.y, parent.y));
            int overlapArea = overlapX * overlapY;

            return (float)overlapArea / childArea;
        }

        #endregion

        #region Step 6: 坐标转换

        private static void ConvertCoordinates(List<LayerInfo> layers, int canvasW, int canvasH)
        {
            foreach (var layer in layers)
            {
                layer.AbsUnityX = layer.Source.x + layer.Source.width / 2f - canvasW / 2f;
                layer.AbsUnityY = canvasH / 2f - (layer.Source.y + layer.Source.height / 2f);
                layer.AbsRotationZ = NormalizeAngle(layer.Source.rotationZ);
            }
        }

        private static void GetRelativePosition(LayerInfo layer, out float relX, out float relY, out float w, out float h)
        {
            float parentAbsX = layer.Parent != null ? layer.Parent.AbsUnityX : 0;
            float parentAbsY = layer.Parent != null ? layer.Parent.AbsUnityY : 0;
            float parentScale = GetLayerScale(layer.Parent);
            float offsetX = layer.AbsUnityX - parentAbsX;
            float offsetY = layer.AbsUnityY - parentAbsY;

            if (layer.Parent != null)
            {
                RotateVector(offsetX, offsetY, -GetLayerRotation(layer.Parent), out offsetX, out offsetY);
            }

            relX = offsetX / parentScale;
            relY = offsetY / parentScale;
            w = GetBaseWidth(layer);
            h = GetBaseHeight(layer);
        }

        #endregion

        #region Step 7: 构建输出

        private static UnityNode BuildUnityNode(LayerInfo layer)
        {
            GetRelativePosition(layer, out float relX, out float relY, out float w, out float h);
            float nodeScale = GetLayerScale(layer);
            float desiredVisualRotationZ = layer.AbsRotationZ;

            var node = new UnityNode
            {
                name = layer.UnityName,
                type = layer.UnityType,
                spritePath = layer.SpritePath ?? "",
                prefabPath = layer.PrefabPath ?? "",
                fillSpritePath = layer.FillSpritePath ?? "",
                x = relX,
                y = relY,
                width = w,
                height = h,
                anchorPreset = (layer.Prefix == "mask" || layer.Prefix == "tile") ? "stretch-all" : "middle-center",
                active = layer.Source.visible,
                order = layer.Source.order,
                opacity = layer.Source.opacity,
                useNativeSize = layer.UseNativeSize,
                imageType = layer.Prefix == "tile" ? "tiled" : (layer.IsSliced ? "sliced" : "simple"),
                raycastTarget = layer.Prefix == "bg" || layer.Prefix == "mask",
                addUIButton = layer.Prefix == "mask",
                addUIButtonEffect = false,
                scaleX = nodeScale,
                scaleY = nodeScale,
                rotationZ = desiredVisualRotationZ,
                children = new List<UnityNode>()
            };

            if (layer.Prefix == "slider")
            {
                node.fillDirection = w >= h ? "horizontal" : "vertical";
            }

            if (layer.Prefix == "text")
            {
                node.text = layer.Source.text ?? "";
                node.fontSize = layer.Source.fontSize;
                node.alignment = MapTextAlignment(layer.Source.textAlignment);
                node.strokeColor = layer.Source.strokeColor ?? "";
                node.strokeWidth = layer.Source.strokeWidth;
            }

            node.colorHex = layer.Source.fontColor ?? "";

            foreach (var child in layer.Children)
                node.children.Add(BuildUnityNode(child));

            return node;
        }

        #endregion

        #region 工具方法

        private static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            var sb = new StringBuilder();
            var parts = input.Split('_');
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpper(part[0]));
                if (part.Length > 1)
                    sb.Append(part.Substring(1));
            }

            return sb.ToString();
        }

        private static string ExtractNameScale(string rawName, bool isTextLayer, out float scale)
        {
            scale = 1f;
            if (string.IsNullOrEmpty(rawName)) return rawName ?? "";

            var lastSlash = rawName.LastIndexOf('/');
            var lastStar = rawName.LastIndexOf('*');
            if (lastStar < 0 || lastStar < lastSlash)
                return rawName;

            var nameWithoutScale = rawName.Substring(0, lastStar);
            var scaleText = rawName.Substring(lastStar + 1);

            if (isTextLayer)
            {
                Debug.LogWarning($"PS2UGUI: 文本图层 '{rawName}' 不支持 *scale，已忽略该缩放配置。");
                return nameWithoutScale;
            }

            if (!float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale) ||
                float.IsNaN(parsedScale) || float.IsInfinity(parsedScale) || parsedScale <= 0f)
            {
                Debug.LogWarning($"PS2UGUI: 图层 '{rawName}' 的 *scale 值无效，已按 1 处理。");
                return nameWithoutScale;
            }

            scale = parsedScale;
            return nameWithoutScale;
        }

        private static float GetLayerScale(LayerInfo layer)
        {
            if (layer == null || layer.Prefix == "text") return 1f;
            return Mathf.Abs(layer.NameScale) <= ScaleEpsilon ? 1f : layer.NameScale;
        }

        private static float GetLayerRotation(LayerInfo layer)
        {
            return layer == null ? 0f : layer.AbsRotationZ;
        }

        private static float GetBaseWidth(LayerInfo layer)
        {
            return layer.Source.width / GetLayerScale(layer);
        }

        private static float GetBaseHeight(LayerInfo layer)
        {
            return layer.Source.height / GetLayerScale(layer);
        }

        private static void RotateVector(float x, float y, float degrees, out float rotatedX, out float rotatedY)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            rotatedX = x * cos - y * sin;
            rotatedY = x * sin + y * cos;
        }

        private static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        private static bool IsGroupPathIgnored(string groupPath)
        {
            if (string.IsNullOrEmpty(groupPath)) return false;

            var segments = groupPath.Split('/');
            foreach (var seg in segments)
            {
                if (seg.StartsWith("#"))
                    return true;
            }

            return false;
        }

        private static string MapTextAlignment(string psAlignment)
        {
            return "center-middle";
        }

        #endregion
    }
}
