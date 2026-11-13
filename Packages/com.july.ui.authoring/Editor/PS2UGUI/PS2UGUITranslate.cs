using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace July.UI.Authoring.Editor
{
    /// <summary>
    /// 将 PS/Figma 导出的布局 JSON 转换为 UnityData 对象
    /// </summary>
    public static class PS2UGUITranslate
    {

        #region 输入数据模型

        [Serializable]
        private class PSData
        {
            public int schemaVersion;
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
            public float x;
            public float y;
            public float width;
            public float height;
            public bool visible;
            public int order;
            public int opacity;
            public string text;
            public float fontSize;
            public string fontColor;
            public string fontName;
            public string textAlignment;
            public bool wordWrap = true;
            public bool fixedCanvas;
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
            public LayerInfo FillLayer;

            public bool Claimed;
            public bool UseNativeSize;
            public bool IsSliced;
            public bool IsTint;

            public float AbsUnityX;
            public float AbsUnityY;
            public float AbsRotationZ;
        }

        #endregion

        #region 公开接口

        internal static UnityData Translate(string psDataJsonPath)
        {
            var jsonText = File.ReadAllText(psDataJsonPath);
            var psData = LitJson.JsonMapper.ToObject<PSData>(jsonText);

            if (psData == null || psData.canvas == null || psData.layers == null)
                throw new FormatException("PS2UGUI: JSON 缺少 canvas 或 layers。");

            if (psData.canvas.width <= 0 || psData.canvas.height <= 0)
                throw new FormatException("PS2UGUI: 画布尺寸无效。");

            return Translate(psData);
        }

        #endregion

        #region 核心转换流程

        private static UnityData Translate(PSData psData)
        {
            int canvasW = psData.canvas.width;
            int canvasH = psData.canvas.height;

            var layers = ClassifyLayers(psData.layers);
            foreach (var layer in layers)
            {
                if (psData.schemaVersion >= 2) layer.UseNativeSize = false;
                if (layer.Prefix != "text" && psData.schemaVersion < 2) layer.Source.opacity = 100;
            }
            PairSliders(layers);
            var activeLayers = FilterActiveLayers(layers);
            AssignUnityNames(layers);
            ConvertCoordinates(activeLayers, canvasW, canvasH);

            return new UnityData
            {
                canvas = new UnityCanvas { width = canvasW, height = canvasH },
                children = BuildFlatNodes(activeLayers)
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
                if (!isTextLayer)
                {
                    fullName = StripRasterScale(fullName);
                    if (layer.fixedCanvas) fullName = StripFixedCanvasSuffix(fullName);
                }
                var info = new LayerInfo { Source = layer };

                if (isTextLayer)
                {
                    info.Prefix = "text";
                    info.ResourceName = fullName;
                    info.SpritePath = "";
                    info.UnityType = "text";
                }
                else
                {
                    var suffixes = "";
                    while (fullName.Length > 0 && "$%@~".IndexOf(fullName[fullName.Length - 1]) >= 0)
                    {
                        suffixes += fullName[fullName.Length - 1];
                        fullName = fullName.Substring(0, fullName.Length - 1);
                    }
                    bool isPrefab = suffixes.Contains("@");
                    bool useNativeSize = suffixes.Contains("$");
                    bool isSliced = suffixes.Contains("%");
                    info.IsTint = suffixes.Contains("~");
                    if (isPrefab && (useNativeSize || isSliced))
                        throw new FormatException($"PS2UGUI: 图层 '{layer.name}' 的 @ 不支持 $ 或 %。");
                    if (useNativeSize && isSliced)
                        throw new FormatException($"PS2UGUI: 图层 '{layer.name}' 的 $ 和 % 不可同时使用。");
                    if (isPrefab) fullName = fullName.Replace('_', '/').Trim('/');
                    if (fullName.Contains("*"))
                        throw new FormatException($"PS2UGUI: * 不再作为倍率后缀，请改用 ^N，例如 img_logo^0.5: {layer.name}");
                    if (fullName.StartsWith("/") || fullName.Contains("\\") || fullName.Contains(":"))
                        throw new FormatException($"PS2UGUI: 资源路径必须是相对路径: {layer.name}");
                    foreach (var segment in fullName.Split('/'))
                        if (segment.Length == 0 || segment == "." || segment == "..")
                            throw new FormatException($"PS2UGUI: 无效的资源路径: {layer.name}");

                    var lastSlash = fullName.LastIndexOf('/');
                    var resourcePart = lastSlash >= 0 ? fullName.Substring(lastSlash + 1) : fullName;
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
                    fillDict[GetGroupScopedResourceKey(layer)] = layer;
            }

            foreach (var layer in layers)
            {
                if (layer.Prefix == "slider" && fillDict.TryGetValue(GetGroupScopedResourceKey(layer), out var fill))
                {
                    layer.FillLayer = fill;
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

                const string baseName = "Tx";

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

        #region Step 5: 构建扁平节点列表

        private static List<UnityNode> BuildFlatNodes(List<LayerInfo> layers)
        {
            var children = new List<UnityNode>(layers.Count);

            foreach (var layer in layers)
                children.Add(BuildUnityNode(layer));

            children.Sort((a, b) => b.order.CompareTo(a.order));
            return children;
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

        #endregion

        #region Step 7: 构建输出

        private static UnityNode BuildUnityNode(LayerInfo layer)
        {
            const float nodeScale = 1f;
            float desiredVisualRotationZ = layer.AbsRotationZ;

            var node = new UnityNode
            {
                name = layer.UnityName,
                type = layer.UnityType,
                spritePath = layer.SpritePath ?? "",
                prefabPath = layer.PrefabPath ?? "",
                fillSpritePath = layer.FillLayer?.SpritePath ?? "",
                fillColorHex = layer.FillLayer != null && layer.FillLayer.IsTint ? layer.FillLayer.Source.fontColor ?? "" : "",
                fillOpacity = layer.FillLayer?.Source.opacity ?? 100,
                x = layer.AbsUnityX,
                y = layer.AbsUnityY,
                width = layer.Source.width,
                height = layer.Source.height,
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
                node.fillDirection = node.width >= node.height ? "horizontal" : "vertical";
            }

            if (layer.Prefix == "text")
            {
                node.text = layer.Source.text ?? "";
                node.fontSize = layer.Source.fontSize;
                node.alignment = MapTextAlignment(layer.Source.textAlignment);
                node.wordWrap = layer.Source.wordWrap;
                node.strokeColor = layer.Source.strokeColor ?? "";
                node.strokeWidth = layer.Source.strokeWidth;
            }

            node.colorHex = layer.Prefix == "text" || layer.IsTint ? layer.Source.fontColor ?? "" : "";

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

        private static string StripRasterScale(string rawName)
        {
            var caret = rawName.LastIndexOf('^');
            if (caret < 0) return rawName;
            var scaleText = rawName.Substring(caret + 1);
            if (!System.Text.RegularExpressions.Regex.IsMatch(scaleText, @"^(?:\d+(?:\.\d*)?|\.\d+)$") ||
                !float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
                float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                throw new FormatException($"PS2UGUI: 节点 PNG 倍率必须是正数: {rawName}");
            return rawName.Substring(0, caret);
        }

        private static string StripFixedCanvasSuffix(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"\[(\d+)[xX×](\d+)\](?=[$%@~]*$)");
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width) || width <= 0 ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height <= 0)
                throw new FormatException($"PS2UGUI: 固定画布命名无效: {name}");
            return name.Remove(match.Index, match.Length);
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

        private static string GetGroupScopedResourceKey(LayerInfo layer)
        {
            return $"{layer.Source.groupPath ?? ""}\n{layer.ResourceName}";
        }

        private static string MapTextAlignment(string psAlignment)
        {
            switch (psAlignment)
            {
                case "left": return "left-middle";
                case "right": return "right-middle";
                default: return "center-middle";
            }
        }

        #endregion
    }
}
