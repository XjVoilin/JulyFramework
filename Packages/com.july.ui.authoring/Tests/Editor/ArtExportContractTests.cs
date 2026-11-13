using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Authoring.Editor.Tests
{
    public class ArtExportContractTests
    {
        private const string AssetsRoot = "Assets/AuthoringContractTest";
        private string _source;

        [SetUp]
        public void SetUp()
        {
            _source = Path.GetFullPath("Library/AuthoringContractTest/Screen");
            Directory.CreateDirectory(_source);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(AssetsRoot);
            Directory.Delete(Path.GetDirectoryName(_source), true);
        }

        private string WriteJson(string name = "img_gray*0.5", string type = "image", string align = "left", int opacity = 50)
        {
            var path = Path.Combine(_source, "Screen_ui_data.json");
            File.WriteAllText(path, "{\"schemaVersion\":2,\"canvas\":{\"width\":1080,\"height\":1920},\"layers\":[{" +
                "\"id\":0,\"name\":\"" + name + "\",\"groupPath\":\"\",\"type\":\"" + type + "\"," +
                "\"x\":8.019607843137255,\"y\":18.03846153846154,\"width\":104.96078431372548,\"height\":54.92307692307692," +
                "\"visible\":true,\"order\":0,\"opacity\":" + opacity + ",\"rotationZ\":0,\"scaleX\":1,\"scaleY\":1," +
                "\"fontColor\":\"#808080\",\"text\":\"Hello\",\"fontSize\":24,\"textAlignment\":\"" + align + "\"}]}");
            return path;
        }

        [Test]
        public void FractionalGeometryAndRasterScalePreserveVisualRectangle()
        {
            var node = PS2UGUITranslate.Translate(WriteJson()).children.Single();
            Assert.That(node.width, Is.EqualTo(104.960784f).Within(0.00001f));
            Assert.That(node.height, Is.EqualTo(54.923077f).Within(0.00001f));
            Assert.That(node.x, Is.EqualTo(8.019608f + node.width / 2f - 540f).Within(0.0001f));
            Assert.That(node.scaleX, Is.EqualTo(1f));
            Assert.That(node.scaleY, Is.EqualTo(1f));
            Assert.That(node.opacity, Is.EqualTo(50));
            Assert.That(node.colorHex, Is.Empty);
        }

        [TestCase("img_gray%~*0.5")]
        [TestCase("img_gray~%*0.5")]
        public void TintAndSliceSuffixesUseTheExportedFilename(string name)
        {
            var node = PS2UGUITranslate.Translate(WriteJson(name)).children.Single();
            Assert.That(node.spritePath, Is.EqualTo("img_gray"));
            Assert.That(node.colorHex, Is.EqualTo("#808080"));
            Assert.That(node.imageType, Is.EqualTo("sliced"));
        }

        [Test]
        public void LegacyNativeSizeSuffixCannotOverrideV2RasterLayout()
        {
            var node = PS2UGUITranslate.Translate(WriteJson("img_gray$*0.5")).children.Single();
            Assert.That(node.useNativeSize, Is.False);
            Assert.That(node.width, Is.EqualTo(104.960784f).Within(0.00001f));
            Assert.That(node.scaleX, Is.EqualTo(1f));
        }

        [Test]
        public void SliderFillRetainsItsOwnTintAndOpacityAfterPairing()
        {
            var path = WriteJson("slider_gray*0.5");
            var json = File.ReadAllText(path);
            var layer = json.Substring(json.IndexOf("[{", StringComparison.Ordinal) + 1);
            layer = layer.Substring(0, layer.Length - 2);
            var fill = layer.Replace("slider_gray*0.5", "sliderfill_gray~*0.5")
                .Replace("\"opacity\":50", "\"opacity\":25").Replace("\"id\":0", "\"id\":1").Replace("\"order\":0", "\"order\":1");
            File.WriteAllText(path, json.Substring(0, json.Length - 2) + "," + fill + "]}");
            WritePng("slider_gray"); WritePng("sliderfill_gray");
            Directory.CreateDirectory(AssetsRoot + "/Res"); AssetDatabase.Refresh();
            var imported = ArtImporter.Import(_source, AssetsRoot + "/Art/Textures");
            Assert.That(imported.success, Is.True, imported.error);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(imported.prefabPath);
            var slider = prefab.GetComponentInChildren<Slider>();
            Assert.That(slider.fillRect.GetComponent<Image>().color.r, Is.EqualTo(128f / 255f).Within(0.00001f));
            Assert.That(slider.fillRect.GetComponent<Image>().color.a, Is.EqualTo(0.25f));
        }

        [TestCase("left", "left-middle")]
        [TestCase("right", "right-middle")]
        [TestCase("center", "center-middle")]
        public void TextAlignmentSurvivesTranslation(string input, string expected)
        {
            Assert.That(PS2UGUITranslate.Translate(WriteJson("Caption", "text", input)).children.Single().alignment,
                Is.EqualTo(expected));
        }

        [TestCase("标题*未完成")]
        [TestCase("Caption*0")]
        public void TextNamesAreNotParsedAsImageRasterScales(string name)
        {
            var node = PS2UGUITranslate.Translate(WriteJson(name, "text")).children.Single();
            Assert.That(node.text, Is.EqualTo("Hello"));
            Assert.That(node.fontSize, Is.EqualTo(24));
        }

        private void WritePng(string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
                texture.Apply();
                File.WriteAllBytes(Path.Combine(_source, name + ".png"), texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(50)]
        [TestCase(98)]
        [TestCase(99)]
        public void ImportCreatesSpriteAndPrefabWithExactLayoutAndSingleOpacity(int opacity)
        {
            WriteJson(opacity: opacity);
            WritePng("img_gray");
            Directory.CreateDirectory(AssetsRoot + "/Res");
            AssetDatabase.Refresh();
            var result = ArtImporter.Import(_source, AssetsRoot + "/Art/Textures");
            Assert.That(result.success, Is.True, result.error);
            Assert.That(result.prefabPath, Is.EqualTo(AssetsRoot + "/Res/Prefabs/UI/Screen/Screen.prefab"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.prefabPath);
            Assert.That(prefab, Is.Not.Null);
            var image = prefab.GetComponentInChildren<Image>();
            Assert.That(image.sprite, Is.Not.Null);
            Assert.That(image.color, Is.EqualTo(new Color(1f, 1f, 1f, opacity / 100f)));
            Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo(104.960784f).Within(0.00001f));
            Assert.That(image.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(image.rectTransform.localEulerAngles.z, Is.EqualTo(0f));
        }

        [Test]
        public void MissingSpriteFailsBeforeCopyOrPrefabGeneration()
        {
            WriteJson("img_missing~*0.5");
            Directory.CreateDirectory(AssetsRoot + "/Res"); AssetDatabase.Refresh();
            var result = ArtImporter.Import(_source, AssetsRoot + "/Art/Textures");
            Assert.That(result.success, Is.False);
            Assert.That(result.missingSprites, Is.EqualTo(new[] { "img_missing.png" }));
            Assert.That(result.prefabPath, Is.Null.Or.Empty);
            Assert.That(Directory.Exists(AssetsRoot + "/Art/Textures"), Is.False);
        }

        [Test]
        public void OutputPathFailureIsNotReportedAsSuccess()
        {
            WriteJson();
            WritePng("img_gray");
            var result = ArtImporter.Import(_source, AssetsRoot + "/Art/Textures");
            Assert.That(result.success, Is.False);
            Assert.That(result.error, Does.Contain("Res/"));
            Assert.That(result.prefabPath, Is.Null.Or.Empty);
            Assert.That(Directory.Exists(AssetsRoot), Is.False);
        }

        [Test]
        public void CancelKeepsExistingPrefabAndOverwritePreservesGuid()
        {
            WriteJson(); WritePng("img_gray");
            Directory.CreateDirectory(AssetsRoot + "/Res");
            AssetDatabase.Refresh();
            var imported = ArtImporter.Import(_source, AssetsRoot + "/Art/Textures");
            Assert.That(imported.success, Is.True, imported.error);
            var bytes = File.ReadAllBytes(imported.prefabPath);
            var guid = AssetDatabase.AssetPathToGUID(imported.prefabPath);
            var json = AssetsRoot + "/Art/Textures/Screen/Screen_ui_data.json";
            var data = PS2UGUITranslate.Translate(json);
            var cancelled = PS2UGUIGenerator.GeneratePrefab(data, json, "Screen", _ => false);
            Assert.That(cancelled.cancelled, Is.True);
            Assert.That(cancelled.success, Is.False);
            Assert.That(File.ReadAllBytes(imported.prefabPath), Is.EqualTo(bytes));
            var saved = PS2UGUIGenerator.GeneratePrefab(data, json, "Screen", _ => true);
            Assert.That(saved.success, Is.True, saved.error);
            Assert.That(AssetDatabase.AssetPathToGUID(imported.prefabPath), Is.EqualTo(guid));
        }

        [Test]
        public void NativePhotoshopExportImportsWithWhiteTintMaskAndSingleOpacity()
        {
            var folder = Environment.GetEnvironmentVariable("ART2UGUI_TEST_EXPORT");
            if (string.IsNullOrEmpty(folder)) Assert.Ignore("Set ART2UGUI_TEST_EXPORT to a native Photoshop Tint export fixture.");
            Directory.CreateDirectory(AssetsRoot + "/Res");
            AssetDatabase.Refresh();
            var imported = ArtImporter.Import(folder, AssetsRoot + "/Art/Textures");
            Assert.That(imported.success, Is.True, imported.error);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(imported.prefabPath);
            var image = prefab.GetComponentInChildren<Image>();
            Assert.That(image.color, Is.EqualTo(new Color(1, 0, 0, 0.5f)));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(204, 204)));
            Assert.That(image.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(image.sprite.rect.size, Is.EqualTo(new Vector2(102, 102)));
        }

        [Test]
        public void NativeRotatedPhotoshopExportKeepsRenderedSizeWithoutSecondRotation()
        {
            var folder = Environment.GetEnvironmentVariable("ART2UGUI_TEST_EXPORT");
            if (string.IsNullOrEmpty(folder)) Assert.Ignore("Set ART2UGUI_TEST_EXPORT to the native Photoshop fixture directory.");
            folder = Path.Combine(Path.GetDirectoryName(folder), "RotatedCase");
            var json = Path.Combine(folder, "RotatedCase_ui_data.json");
            var expected = PS2UGUITranslate.Translate(json).children.Single();
            Directory.CreateDirectory(AssetsRoot + "/Res"); AssetDatabase.Refresh();
            var imported = ArtImporter.Import(folder, AssetsRoot + "/Art/Textures");
            Assert.That(imported.success, Is.True, imported.error);
            var image = AssetDatabase.LoadAssetAtPath<GameObject>(imported.prefabPath).GetComponentInChildren<Image>();
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(expected.width, expected.height)));
            Assert.That(image.rectTransform.localEulerAngles.z, Is.EqualTo(0));
            Assert.That(image.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(image.color.a, Is.EqualTo(0.5f));
            Assert.That(image.sprite.rect.size, Is.EqualTo(new Vector2(59, 50)));
        }
    }
}
