using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI.Tests
{
    public sealed class UIDisabledVisualTests
    {
        private GameObject _root;
        private UIDisabledVisual _visual;
        private Image _image, _other;
        private TMP_Text _text;
        private GameObject _container, _businessChild;
        private readonly List<Object> _assets = new();
        private Sprite _normal, _disabled;
        private Material _normalMaterial, _disabledMaterial;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (var asset in _assets) Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        [UnityTest]
        public IEnumerator AllBindings_RestoreAuthoredNormal_AndPreserveBusinessVisibility()
        {
            Create();
            var initialMaterialColor = _disabledMaterial.GetColor(ShaderUtilities.ID_FaceColor);
            _root.SetActive(true);
            yield return null;
            _visual.SetDisabled(true);
            Assert.That(_image.sprite, Is.SameAs(_disabled));
            Assert.That(_other.sprite, Is.SameAs(_disabled));
            Assert.That(_container.activeSelf, Is.False);
            Assert.That(_text.color, Is.EqualTo(Color.gray));
            Assert.That(_text.fontSharedMaterial, Is.SameAs(_disabledMaterial));
            Assert.That(_text.text, Is.EqualTo("42"));

            _image.sprite = null;
            _visual.SetDisabled(true);
            Assert.That(_image.sprite, Is.SameAs(_disabled));
            _visual.SetDisabled(false);
            Assert.That(_image.sprite, Is.SameAs(_normal));
            Assert.That(_other.sprite, Is.SameAs(_normal));
            Assert.That(_text.color, Is.EqualTo(Color.yellow));
            Assert.That(_text.fontSharedMaterial, Is.SameAs(_normalMaterial));
            Assert.That(_container.activeSelf, Is.True);
            Assert.That(_businessChild.activeSelf, Is.False);
            Assert.That(_disabledMaterial.GetColor(ShaderUtilities.ID_FaceColor), Is.EqualTo(initialMaterialColor));
        }

        [UnityTest]
        public IEnumerator Button_InitialDisabled_InactiveUpdates_Reopen_AndClickPermission()
        {
            Create();
            var button = _root.AddComponent<UISmartButton>();
            SetField(button, "_disabledVisual", _visual);
            SetField(button, "_interactable", false);
            button.enableCooldown = false;
            button.enableSound = false;
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);
            _root.SetActive(true);
            yield return null;
            Assert.That(_visual.IsDisabled, Is.True);
            Assert.That(_image.sprite, Is.SameAs(_disabled));
            button.OnPointerDown(new PointerEventData(null));
            button.OnPointerClick(new PointerEventData(null));
            Assert.That(clicks, Is.Zero);
            Assert.That(button.transform.localScale, Is.EqualTo(Vector3.one));

            _root.SetActive(false);
            button.SetInteractable(true);
            Assert.That(_image.sprite, Is.SameAs(_normal));
            _root.SetActive(true);
            yield return null;
            Assert.That(_visual.IsDisabled, Is.False);
            button.OnPointerClick(new PointerEventData(null));
            Assert.That(clicks, Is.EqualTo(1));

            _root.SetActive(false);
            button.SetInteractable(false);
            _image.sprite = _normal;
            _root.SetActive(true);
            yield return null;
            Assert.That(_image.sprite, Is.SameAs(_disabled));
            _image.sprite = null;
            button.SetInteractable(false);
            Assert.That(_image.sprite, Is.SameAs(_disabled));
        }

        [UnityTest]
        public IEnumerator StandaloneGray_DoesNotBlockClick_AndSurvivesReopen()
        {
            Create();
            var button = _root.AddComponent<UISmartButton>();
            button.enableCooldown = false;
            button.enableSound = false;
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);
            _root.SetActive(true);
            yield return null;
            _visual.SetDisabled(true);
            button.OnPointerClick(new PointerEventData(null));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(button.IsInteractable, Is.True);
            _root.SetActive(false);
            _root.SetActive(true);
            yield return null;
            Assert.That(_visual.IsDisabled, Is.True);
            Assert.That(_text.fontSharedMaterial, Is.SameAs(_disabledMaterial));
        }

        [UnityTest]
        public IEnumerator SetDisabledBeforeFirstAwake_PreservesAuthoredScale()
        {
            Create();
            _root.transform.localScale = new Vector3(2, 3, 1);
            var button = _root.AddComponent<UISmartButton>();
            SetField(button, "_disabledVisual", _visual);
            button.SetInteractable(false);
            Assert.That(_root.transform.localScale, Is.EqualTo(new Vector3(2, 3, 1)));
            _root.SetActive(true);
            yield return null;
            Assert.That(_root.transform.localScale, Is.EqualTo(new Vector3(2, 3, 1)));
            Assert.That(_image.sprite, Is.SameAs(_disabled));
        }

        private void Create()
        {
            _root = new GameObject("Disabled visual", typeof(RectTransform));
            _root.SetActive(false);
            _visual = _root.AddComponent<UIDisabledVisual>();
            _image = Child("Background", _root).AddComponent<Image>();
            _other = Child("Icon", _root).AddComponent<Image>();
            _text = Child("Title", _root).AddComponent<TextMeshProUGUI>();
            _text.font = TMP_Settings.defaultFontAsset;
            Assert.That(_text.font, Is.Not.Null, "测试宿主需要 TMP 默认 SDF 字体。");
            _text.text = "42";
            _container = Child("Badge container", _root);
            _businessChild = Child("Business quantity", _container);
            _businessChild.SetActive(false);
            _normal = Own(Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero));
            _disabled = Own(Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero));
            _normalMaterial = Own(new Material(_text.font.material));
            _disabledMaterial = Own(new Material(_text.font.material));
            var doubleOutline = Shader.Find("JulyGF/UI/TextMeshPro Double Outline");
            Assert.That(doubleOutline, Is.Not.Null, "测试宿主需要 July 双描边 Shader。");
            _disabledMaterial.shader = doubleOutline;
            Bind(_visual, "_sprites", (_image, (object)_normal, _disabled), (_other, _normal, _disabled));
            Bind(_visual, "_activeObjects", (_container, (object)true, false));
            Bind(_visual, "_colors", (_text, (object)Color.yellow, Color.gray));
            Bind(_visual, "_textMaterials", (_text, (object)_normalMaterial, _disabledMaterial));
        }

        private T Own<T>(T asset) where T : Object { _assets.Add(asset); return asset; }

        private static GameObject Child(string name, GameObject parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Bind(UIDisabledVisual visual, string name, params (Object target, object normal, object disabled)[] items)
        {
            var field = typeof(UIDisabledVisual).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            var elementType = field.FieldType.GetElementType();
            var array = Array.CreateInstance(elementType, items.Length);
            for (var i = 0; i < items.Length; i++)
            {
                var entry = Activator.CreateInstance(elementType);
                elementType.GetField("_target").SetValue(entry, items[i].target);
                elementType.GetField("_normal").SetValue(entry, items[i].normal);
                elementType.GetField("_disabled").SetValue(entry, items[i].disabled);
                array.SetValue(entry, i);
            }
            field.SetValue(visual, array);
        }
    }
}
