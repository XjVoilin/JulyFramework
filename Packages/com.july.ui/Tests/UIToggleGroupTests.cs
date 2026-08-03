using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIToggleGroupTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void SetWithoutNotify_UpdatesItemsAndContents()
        {
            var group = CreateGroup(out var items);
            var contents = new List<GameObject>
            {
                new("Content0"),
                new("Content1")
            };
            contents.ForEach(content => content.transform.SetParent(_root.transform));
            SetField(group, "m_Contents", contents);

            group.SetWithoutNotify(1);

            Assert.That(items[0].IsOn, Is.False);
            Assert.That(items[1].IsOn, Is.True);
            Assert.That(contents[0].activeSelf, Is.False);
            Assert.That(contents[1].activeSelf, Is.True);
        }

        [Test]
        public void EmptyContents_PreservesSelectionOnlyUsage()
        {
            var group = CreateGroup(out var items);

            group.SetWithoutNotify(1);

            Assert.That(items[0].IsOn, Is.False);
            Assert.That(items[1].IsOn, Is.True);
        }

        [Test]
        public void SetWithoutNotify_ReappliesCurrentSelection()
        {
            var group = CreateGroup(out var items);
            var contents = new List<GameObject>
            {
                new("Content0"),
                new("Content1")
            };
            contents.ForEach(content => content.transform.SetParent(_root.transform));
            contents[0].SetActive(false);
            SetField(group, "m_Contents", contents);

            group.SetWithoutNotify(0);

            Assert.That(items[0].IsOn, Is.True);
            Assert.That(items[1].IsOn, Is.False);
            Assert.That(contents[0].activeSelf, Is.True);
            Assert.That(contents[1].activeSelf, Is.False);
        }

        [Test]
        public void MismatchedContents_ThrowsBeforeChangingSelection()
        {
            var group = CreateGroup(out var items);
            var content = new GameObject("Content0");
            content.transform.SetParent(_root.transform);
            SetField(group, "m_Contents", new List<GameObject> { content });

            Assert.Throws<InvalidOperationException>(() => group.SetWithoutNotify(1));
            Assert.That(group.SelectedIndex, Is.Zero);
            Assert.That(items[0].IsOn, Is.False);
            Assert.That(items[1].IsOn, Is.False);
            Assert.That(content.activeSelf, Is.True);
        }

        private UIToggleGroup CreateGroup(out List<UIToggleItem> items)
        {
            _root = new GameObject("UIToggleGroup");
            var group = _root.AddComponent<UIToggleGroup>();
            items = new List<UIToggleItem>
            {
                CreateItem("Item0"),
                CreateItem("Item1")
            };
            SetField(group, "m_Items", items);
            return group;
        }

        private UIToggleItem CreateItem(string name)
        {
            var itemObject = new GameObject(name);
            itemObject.transform.SetParent(_root.transform);
            return itemObject.AddComponent<UIToggleItem>();
        }

        private static void SetField<T>(UIToggleGroup group, string name, T value)
        {
            typeof(UIToggleGroup)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(group, value);
        }
    }
}
