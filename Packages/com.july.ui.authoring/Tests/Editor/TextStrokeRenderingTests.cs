using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace July.UI.Authoring.Editor.Tests
{
    public class TextStrokeRenderingTests
    {
        private const string Root = "Assets/TextStrokeContractTest";

        [TestCase(42f, 3f)]
        [TestCase(88f, 8f)]
        [TestCase(140f, 6f)]
        [TestCase(170.32f, 16f)]
        public void GeneratedStrokeKeepsItsPixelWidthAcrossFontSizes(float fontSize, float width)
        {
            var fontPath = Environment.GetEnvironmentVariable("ART2UGUI_TEST_FONT_ASSET");
            if (string.IsNullOrEmpty(fontPath)) Assert.Ignore("Set ART2UGUI_TEST_FONT_ASSET to an imported SDF font fixture.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Pixel rendering requires a graphics device; omit -nographics.");
            var settings = PS2UGUISettings.Instance;
            var previousFont = settings.DefaultFontAssetPath;
            var previousMaterials = settings.FontMaterialPath;
            GameObject canvas = null, cameraObject = null, node = null;
            RenderTexture target = null;
            try
            {
                settings.DefaultFontAssetPath = fontPath;
                settings.FontMaterialPath = Root + "/Materials/";
                Directory.CreateDirectory(settings.FontMaterialPath);
                AssetDatabase.Refresh();
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(settings.DefaultFontAssetPath);
                Assert.That(font, Is.Not.Null);
                var data = new UnityNode { name = "Stroke", text = "I", fontSize = fontSize,
                    strokeColor = "#000000", strokeWidth = width, opacity = 100,
                    colorHex = "#FFFFFF", alignment = "center-middle", wordWrap = false };
                node = (GameObject)typeof(PS2UGUIGenerator).GetMethod("CreateTextNode", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { data });
                var tmp = node.GetComponent<TextMeshProUGUI>();
                canvas = new GameObject("StrokeCanvas", typeof(Canvas));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                canvas.layer = 31;
                canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(512, 256);
                node.layer = 31;
                node.transform.SetParent(canvas.transform, false);
                tmp.rectTransform.sizeDelta = new Vector2(500, 250);
                tmp.rectTransform.anchoredPosition = Vector2.zero;
                cameraObject = new GameObject("StrokeCamera", typeof(Camera));
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 128;
                camera.transform.position = new Vector3(0, 0, -100);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << 31;
                target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                var outlined = RenderBounds(camera, tmp, target);
                var generated = tmp.fontSharedMaterial;
                tmp.fontSharedMaterial = font.material;
                var plain = RenderBounds(camera, tmp, target);
                var actual = (outlined - plain) / 2f;
                TestContext.WriteLine($"fontSize={fontSize}; expected stroke={width}px; rendered={actual}px; dilate={generated.GetFloat("_UnderlayDilate")}; pointSize={font.faceInfo.pointSize}");
                Assert.That(actual, Is.EqualTo(width).Within(1f));
                Assert.That(generated.GetFloat("_UnderlayDilate"), Is.InRange(0f, 1f));
                var token = generated.GetFloat("_UnderlayDilate").ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "");
                Assert.That(generated.name, Is.EqualTo("Font_Main 000000 " + token));
            }
            finally
            {
                if (node != null) UnityEngine.Object.DestroyImmediate(node);
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                settings.DefaultFontAssetPath = previousFont;
                settings.FontMaterialPath = previousMaterials;
                AssetDatabase.DeleteAsset(Root);
            }
        }

        [Test]
        public void MaterialNamesUseTheActualParameterAndReuseOnlyMatchingMaterials()
        {
            var fontPath = Environment.GetEnvironmentVariable("ART2UGUI_TEST_FONT_ASSET");
            if (string.IsNullOrEmpty(fontPath)) Assert.Ignore("Set ART2UGUI_TEST_FONT_ASSET to an imported SDF font fixture.");
            var settings = PS2UGUISettings.Instance;
            var previousFont = settings.DefaultFontAssetPath;
            var previousMaterials = settings.FontMaterialPath;
            var nodes = new System.Collections.Generic.List<GameObject>();
            try
            {
                settings.DefaultFontAssetPath = fontPath;
                settings.FontMaterialPath = Root + "/Materials/";
                Directory.CreateDirectory(settings.FontMaterialPath);
                AssetDatabase.Refresh();
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                var stale = new Material(font.material);
                stale.EnableKeyword("UNDERLAY_ON");
                stale.SetFloat("_UnderlayDilate", 0.8f);
                var stalePath = settings.FontMaterialPath + "Font_Main 000000 04.mat";
                AssetDatabase.CreateAsset(stale, stalePath);
                var staleGuid = AssetDatabase.AssetPathToGUID(stalePath);
                var method = typeof(PS2UGUIGenerator).GetMethod("CreateTextNode", BindingFlags.NonPublic | BindingFlags.Static);
                using (var scaleMaterial = new MaterialScope(font.material))
                {
                    ShaderUtilities.UpdateShaderRatios(scaleMaterial.Value);
                    var pixelRange = scaleMaterial.Value.GetFloat("_GradientScale") * scaleMaterial.Value.GetFloat("_ScaleRatioC") *
                        (88f / font.faceInfo.pointSize * font.faceInfo.scale);
                    foreach (var parameter in new[] { 0.4f, 0.4f, 0.45f })
                    {
                        var data = new UnityNode { name = "Stroke", text = "I", fontSize = 88,
                            strokeColor = "#000000", strokeWidth = pixelRange * parameter, opacity = 100 };
                        nodes.Add((GameObject)method.Invoke(null, new object[] { data }));
                    }
                }
                var first = nodes[0].GetComponent<TextMeshProUGUI>().fontSharedMaterial;
                var repeated = nodes[1].GetComponent<TextMeshProUGUI>().fontSharedMaterial;
                var second = nodes[2].GetComponent<TextMeshProUGUI>().fontSharedMaterial;
                Assert.That(first.name, Is.EqualTo("Font_Main 000000 04 1"));
                Assert.That(first.GetFloat("_UnderlayDilate"), Is.EqualTo(0.4f));
                Assert.That(second.name, Is.EqualTo("Font_Main 000000 045"));
                Assert.That(second.GetFloat("_UnderlayDilate"), Is.EqualTo(0.45f));
                Assert.That(AssetDatabase.GetAssetPath(repeated), Is.EqualTo(AssetDatabase.GetAssetPath(first)));
                Assert.That(stale.GetFloat("_UnderlayDilate"), Is.EqualTo(0.8f), "Do not modify old materials referenced by other prefabs");
                Assert.That(AssetDatabase.AssetPathToGUID(stalePath), Is.EqualTo(staleGuid));
            }
            finally
            {
                foreach (var node in nodes) UnityEngine.Object.DestroyImmediate(node);
                settings.DefaultFontAssetPath = previousFont;
                settings.FontMaterialPath = previousMaterials;
                AssetDatabase.DeleteAsset(Root);
            }
        }

        private sealed class MaterialScope : IDisposable
        {
            public Material Value { get; }
            public MaterialScope(Material source) { Value = new Material(source); }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(Value); }
        }

        [Test]
        public void DifferentFontSizesAndFractionalWidthsDoNotReuseTheSameStrokeMaterial()
        {
            var fontPath = Environment.GetEnvironmentVariable("ART2UGUI_TEST_FONT_ASSET");
            if (string.IsNullOrEmpty(fontPath)) Assert.Ignore("Set ART2UGUI_TEST_FONT_ASSET to an imported SDF font fixture.");
            var settings = PS2UGUISettings.Instance;
            var previousFont = settings.DefaultFontAssetPath;
            var previousMaterials = settings.FontMaterialPath;
            var nodes = new System.Collections.Generic.List<GameObject>();
            try
            {
                settings.DefaultFontAssetPath = fontPath;
                settings.FontMaterialPath = Root + "/Materials/";
                Directory.CreateDirectory(settings.FontMaterialPath);
                AssetDatabase.Refresh();
                foreach (var pair in new[] { new Vector2(42, 1), new Vector2(84, 1), new Vector2(42, 0.1f) })
                {
                    var data = new UnityNode { name = "Stroke", text = "I", fontSize = pair.x,
                        strokeColor = "#000000", strokeWidth = pair.y, opacity = 100 };
                    nodes.Add((GameObject)typeof(PS2UGUIGenerator).GetMethod("CreateTextNode", BindingFlags.NonPublic | BindingFlags.Static)
                        .Invoke(null, new object[] { data }));
                }
                var first = nodes[0].GetComponent<TextMeshProUGUI>().fontSharedMaterial;
                var second = nodes[1].GetComponent<TextMeshProUGUI>().fontSharedMaterial;
                var fractional = nodes[2].GetComponent<TextMeshProUGUI>().fontSharedMaterial;
                Assert.That(AssetDatabase.GetAssetPath(first), Is.Not.EqualTo(AssetDatabase.GetAssetPath(second)), "The same pixel width needs different dilation at different font sizes");
                Assert.That(AssetDatabase.GetAssetPath(first), Is.Not.EqualTo(AssetDatabase.GetAssetPath(fractional)), "1px and 0.1px must have different material paths");
                Assert.That(first.GetFloat("_UnderlayDilate"), Is.EqualTo(second.GetFloat("_UnderlayDilate") * 2).Within(0.0016f));
            }
            finally
            {
                foreach (var node in nodes) UnityEngine.Object.DestroyImmediate(node);
                settings.DefaultFontAssetPath = previousFont;
                settings.FontMaterialPath = previousMaterials;
                AssetDatabase.DeleteAsset(Root);
            }
        }

        private static int RenderBounds(Camera camera, TextMeshProUGUI text, RenderTexture target)
        {
            text.UpdateMeshPadding();
            text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
                var pixels = texture.GetPixels32();
                var left = target.width;
                var right = -1;
                for (var y = 0; y < target.height; y++)
                    for (var x = 0; x < target.width; x++)
                        if (pixels[y * target.width + x].a >= 128)
                        { left = Math.Min(left, x); right = Math.Max(right, x); }
                Assert.That(right, Is.GreaterThanOrEqualTo(left), "No rendered glyph pixels");
                return right - left + 1;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
