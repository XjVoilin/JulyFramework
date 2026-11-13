using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI.Editor.Tests
{
    public sealed class UIDisabledVisualEditorTests
    {
        private GameObject _root, _child;
        private UIDisabledVisual _visual;
        private Image _image;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Preview root", typeof(RectTransform));
            _root.SetActive(false);
            _visual = _root.AddComponent<UIDisabledVisual>();
            _child = new GameObject("Image", typeof(RectTransform), typeof(Image));
            _child.transform.SetParent(_root.transform, false);
            _image = _child.GetComponent<Image>();
            Configure("_colors", _image, Color.yellow, Color.gray);
        }

        [TearDown]
        public void TearDown()
        {
            UIDisabledVisualPreview.Stop();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Preview_RestoresActualSceneValues_WithoutChangingInitialState()
        {
            _image.color = Color.cyan; // 现场不必等于配置的正常态。
            Configure("_activeObjects", _child, true, false);
            UIDisabledVisualPreview.Show(_visual, true);
            Assert.That(_image.color, Is.EqualTo(Color.gray));
            Assert.That(_child.activeSelf, Is.False);
            Assert.That(_visual.IsDisabled, Is.False);
            UIDisabledVisualPreview.Show(_visual, false);
            Assert.That(_image.color, Is.EqualTo(Color.yellow));
            Assert.That(_child.activeSelf, Is.True);
            UIDisabledVisualPreview.Stop();
            Assert.That(_image.color, Is.EqualTo(Color.cyan));
            Assert.That(_child.activeSelf, Is.True);
            Assert.That(AnimationMode.InAnimationMode(), Is.False);
        }

        [Test]
        public void Preview_StopsBeforeSceneSave_AndLeavesSavedColorUnchanged()
        {
            var scene = _root.scene;
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/JulyDisabledVisualPreviewTest.unity");
            try
            {
                _image.color = Color.cyan;
                UIDisabledVisualPreview.Show(_visual, true);
                Assert.That(EditorSceneManager.SaveScene(scene, path, true), Is.True);
                Assert.That(UIDisabledVisualPreview.Owner, Is.Null);
                Assert.That(_image.color, Is.EqualTo(Color.cyan));
                Assert.That(System.IO.File.ReadAllText(path), Does.Not.Contain("m_Color: {r: 0.5, g: 0.5, b: 0.5, a: 1}"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void CaptureNormal_UsesCurrentValues_AndSupportsUndo()
        {
            _image.color = Color.cyan;
            UIDisabledVisualAuthoring.CaptureNormal(_visual);
            _visual.SetDisabled(false);
            Assert.That(_image.color, Is.EqualTo(Color.cyan));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            _visual.SetDisabled(false);
            Assert.That(_image.color, Is.EqualTo(Color.yellow));
        }

        [Test]
        public void Preview_RestoresSpriteAndFontMaterialReferences()
        {
            var original = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var disabled = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var node = new GameObject("Text", typeof(RectTransform));
            node.transform.SetParent(_root.transform, false);
            var text = node.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            var normalMaterial = text.font.material;
            var disabledMaterial = new Material(normalMaterial);
            try
            {
                _image.sprite = original;
                text.fontSharedMaterial = normalMaterial;
                Configure("_sprites", _image, original, disabled);
                Configure("_textMaterials", text, normalMaterial, disabledMaterial);
                UIDisabledVisualPreview.Show(_visual, true);
                Assert.That(_image.sprite, Is.SameAs(disabled));
                Assert.That(text.fontSharedMaterial, Is.SameAs(disabledMaterial));
                UIDisabledVisualPreview.Stop();
                Assert.That(_image.sprite, Is.SameAs(original));
                Assert.That(text.fontSharedMaterial, Is.SameAs(normalMaterial));
            }
            finally
            {
                UIDisabledVisualPreview.Stop();
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(disabled);
                Object.DestroyImmediate(disabledMaterial);
            }
        }

        [Test]
        public void LeavingInspector_StopsPreview_AndOtherAnimationModeIsPreserved()
        {
            var inspector = UnityEditor.Editor.CreateEditor(_visual);
            _image.color = Color.cyan;
            UIDisabledVisualPreview.Show(_visual, true);
            Object.DestroyImmediate(inspector);
            Assert.That(_image.color, Is.EqualTo(Color.cyan));
            Assert.That(AnimationMode.InAnimationMode(), Is.False);
            AnimationMode.StartAnimationMode();
            try
            {
                Assert.Throws<System.InvalidOperationException>(() => UIDisabledVisualPreview.Show(_visual, true));
                Assert.That(AnimationMode.InAnimationMode(), Is.True);
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        [Test]
        public void Validation_RejectsSelfVisibility_MissingTarget_AndDuplicateProperty()
        {
            Configure("_activeObjects", _root, true, false);
            Assert.That(UIDisabledVisualAuthoring.ConfigurationErrors(_visual).Any(e => e.Contains("this GameObject or an ancestor")), Is.True);
            Configure("_activeObjects", null, true, false);
            Assert.That(UIDisabledVisualAuthoring.ConfigurationErrors(_visual).Any(e => e.Contains("missing target")), Is.True);
            var source = new SerializedObject(_visual);
            source.FindProperty("_colors").arraySize = 2;
            source.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(UIDisabledVisualAuthoring.ConfigurationErrors(_visual).Any(e => e.Contains("duplicate binding")), Is.True);
        }

        [Test]
        public void Validation_ReportsOtherVisualAndLegacyGrayConflicts()
        {
            var other = _child.AddComponent<UIDisabledVisual>();
            var source = new SerializedObject(other);
            source.FindProperty("_colors").arraySize = 1;
            source.FindProperty("_colors").GetArrayElementAtIndex(0).FindPropertyRelative("_target").objectReferenceValue = _image;
            source.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(UIDisabledVisualAuthoring.ConfigurationErrors(_visual).Any(e => e.Contains("is also controlled")), Is.True);
            _root.AddComponent<UIGrayGroup>();
            Assert.That(UIDisabledVisualAuthoring.ConfigurationErrors(_visual).Any(e => e.Contains("UIGrayGroup")), Is.True);
        }

        [Test]
        public void Validation_RejectsWrongFontAtlas()
        {
            var node = new GameObject("Text", typeof(RectTransform));
            node.transform.SetParent(_root.transform, false);
            var text = node.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            Assert.That(text.font, Is.Not.Null);
            var wrong = new Material(text.font.material) { mainTexture = Texture2D.blackTexture };
            try
            {
                Configure("_textMaterials", text, text.font.material, wrong);
                Assert.That(UIDisabledVisualAuthoring.ConfigurationErrors(_visual).Any(e => e.Contains("atlas texture")), Is.True);
            }
            finally { Object.DestroyImmediate(wrong); }
        }

        private void Configure(string arrayName, Object target, object normal, object disabled)
        {
            var source = new SerializedObject(_visual);
            var array = source.FindProperty(arrayName);
            array.arraySize = 1;
            var entry = array.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("_target").objectReferenceValue = target;
            SetValue(entry.FindPropertyRelative("_normal"), normal);
            SetValue(entry.FindPropertyRelative("_disabled"), disabled);
            source.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetValue(SerializedProperty property, object value)
        {
            if (value is Color color) property.colorValue = color;
            else if (value is bool active) property.boolValue = active;
            else property.objectReferenceValue = (Object)value;
        }
    }
}
