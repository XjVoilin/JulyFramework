using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UISmartButtonSpriteTests
    {
        private GameObject _root;
        private Sprite _normal;
        private Sprite _disabled;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_normal);
            Object.DestroyImmediate(_disabled);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InitialSerializedState_SelectsMatchingSprite(bool interactable)
        {
            var button = Create(interactable, out var image);
            Assert.That(button.IsInteractable, Is.EqualTo(interactable));
            Assert.That(image.sprite, Is.SameAs(interactable ? _normal : _disabled));
        }

        [Test]
        public void DisabledState_BlocksPressAndClick_ThenCanBeEnabled()
        {
            var button = Create(false, out var image);
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);
            var scale = button.transform.localScale;
            button.OnPointerDown(new PointerEventData(null));
            button.OnPointerClick(new PointerEventData(null));
            Assert.That(clicks, Is.Zero);
            Assert.That(button.transform.localScale, Is.EqualTo(scale));
            Assert.That(image.sprite, Is.SameAs(_disabled));

            button.SetInteractable(true);
            button.OnPointerClick(new PointerEventData(null));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(image.sprite, Is.SameAs(_normal));

            button.SetInteractable(false);
            button.OnPointerClick(new PointerEventData(null));
            Assert.That(clicks, Is.EqualTo(1));
        }

        [Test]
        public void Reopen_ReappliesCurrentStateWithoutChangingOtherImages()
        {
            var button = Create(false, out var image);
            var child = new GameObject("Unrelated Image", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(_root.transform);
            child.GetComponent<Image>().sprite = _normal;
            _root.SetActive(false);
            image.sprite = _normal;
            _root.SetActive(true);
            Assert.That(image.sprite, Is.SameAs(_disabled));
            Assert.That(child.GetComponent<Image>().sprite, Is.SameAs(_normal));
            button.SetInteractable(true);
            _root.SetActive(false);
            _root.SetActive(true);
            Assert.That(image.sprite, Is.SameAs(_normal));
        }

        [Test]
        public void ReapplyingSameState_RefreshesAssignedImage()
        {
            var button = Create(false, out var image);
            image.sprite = _normal;
            button.SetInteractable(false);
            Assert.That(image.sprite, Is.SameAs(_disabled));
        }

        private UISmartButtonSprite Create(bool interactable, out Image image)
        {
            _root = new GameObject("Sprite Button", typeof(RectTransform), typeof(Image));
            _root.SetActive(false);
            image = _root.GetComponent<Image>();
            _normal = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _disabled = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var button = _root.AddComponent<UISmartButtonSprite>();
            SetField(button, "_image", image);
            SetField(button, "_normalSprite", _normal);
            SetField(button, "_disabledSprite", _disabled);
            typeof(UISmartButton).GetField("_interactable", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(button, interactable);
            button.enableSound = false;
            button.enableCooldown = false;
            _root.SetActive(true);
            return button;
        }

        private static void SetField(UISmartButtonSprite button, string name, object value)
            => typeof(UISmartButtonSprite).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(button, value);
    }
}
