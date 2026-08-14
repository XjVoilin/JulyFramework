using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using July.Arch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIToggleGroupTests
    {
        private GameObject _root;
        private ArchContext _context;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            _context?.Shutdown();
            _context = null;
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
            SetField(group, "_contents", contents);

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
            SetField(group, "_contents", contents);

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
            SetField(group, "_contents", new List<GameObject> { content });

            Assert.Throws<InvalidOperationException>(() => group.SetWithoutNotify(1));
            Assert.That(group.SelectedIndex, Is.Zero);
            Assert.That(items[0].IsOn, Is.False);
            Assert.That(items[1].IsOn, Is.False);
            Assert.That(content.activeSelf, Is.True);
        }

        [Test]
        public void ClickWithoutFactory_CommitsSelectionAndNotifies()
        {
            var group = CreateGroup(out var items);
            var contents = CreateContents();
            SetField(group, "_contents", contents);
            group.SetWithoutNotify(0);

            var changedIndex = -1;
            group.OnValueChanged += index => changedIndex = index;

            group.NotifyItemClicked(items[1]);

            Assert.That(changedIndex, Is.EqualTo(1));
            Assert.That(group.SelectedIndex, Is.EqualTo(1));
            Assert.That(items[0].IsOn, Is.False);
            Assert.That(items[1].IsOn, Is.True);
            Assert.That(contents[0].activeSelf, Is.False);
            Assert.That(contents[1].activeSelf, Is.True);
        }

        [Test]
        public async Task Click_WaitsForFactoryProcedureBeforeCommitting()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            var procedure = new PendingProcedure();
            group.SetProcedureFactory(_ => procedure);

            group.NotifyItemClicked(items[1]);

            Assert.That(group.SelectedIndex, Is.Zero);
            Assert.That(items[0].IsOn, Is.True);
            Assert.That(items[1].IsOn, Is.False);

            procedure.Complete();
            await UniTask.Yield();

            Assert.That(group.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public void Click_WithoutFactoryCommitsImmediately()
        {
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);

            group.NotifyItemClicked(items[1]);

            Assert.That(group.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public void Click_RequestsFreshProcedureForEachSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            var procedures = new List<ProcedureBase>();
            group.SetProcedureFactory(_ =>
            {
                var procedure = new CompletedProcedure();
                procedures.Add(procedure);
                return procedure;
            });

            group.NotifyItemClicked(items[1]);
            group.NotifyItemClicked(items[0]);

            Assert.That(procedures, Has.Count.EqualTo(2));
            Assert.That(procedures[0], Is.Not.SameAs(procedures[1]));
        }

        [Test]
        public async Task NewerClick_SupersedesPendingProcedure()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items, 3);
            group.SetWithoutNotify(0);
            var pending = new PendingProcedure();
            group.SetProcedureFactory(index => index == 1 ? pending : null);

            group.NotifyItemClicked(items[1]);
            group.NotifyItemClicked(items[2]);
            pending.Complete();
            await UniTask.Yield();

            Assert.That(group.SelectedIndex, Is.EqualTo(2));
        }

        [Test]
        public async Task SetWithoutNotify_CancelsPendingProcedureSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            var pending = new PendingProcedure();
            group.SetProcedureFactory(_ => pending);

            group.NotifyItemClicked(items[1]);
            group.SetWithoutNotify(0);
            pending.Complete();
            await UniTask.Yield();

            Assert.That(group.SelectedIndex, Is.Zero);
        }

        [Test]
        public async Task SetProcedureFactory_CancelsPendingSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            var pending = new PendingProcedure();
            group.SetProcedureFactory(_ => pending);

            group.NotifyItemClicked(items[1]);
            group.SetProcedureFactory(null);
            pending.Complete();
            await UniTask.Yield();

            Assert.That(group.SelectedIndex, Is.Zero);
        }

        [Test]
        public void Click_ProcedureFailureIsLoggedAndKeepsCurrentSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            group.SetProcedureFactory(_ => new FailingProcedure());

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: Preparation failed."));
            group.NotifyItemClicked(items[1]);

            Assert.That(group.SelectedIndex, Is.Zero);
        }

        private UIToggleGroup CreateGroup(out List<UIToggleItem> items, int itemCount = 2)
        {
            _root = new GameObject("UIToggleGroup");
            var group = _root.AddComponent<UIToggleGroup>();
            items = new List<UIToggleItem>(itemCount);
            for (var i = 0; i < itemCount; i++)
                items.Add(CreateItem($"Item{i}"));
            SetField(group, "_items", items);
            return group;
        }

        private void InitializeArchitecture()
        {
            _context = new ArchContext();
            _context.InitializeAsync().GetAwaiter().GetResult();
        }

        private UIToggleItem CreateItem(string name)
        {
            var itemObject = new GameObject(name);
            itemObject.transform.SetParent(_root.transform);
            return itemObject.AddComponent<UIToggleItem>();
        }

        private List<GameObject> CreateContents()
        {
            var contents = new List<GameObject>
            {
                new("Content0"),
                new("Content1")
            };
            contents.ForEach(content => content.transform.SetParent(_root.transform));
            return contents;
        }

        private static void SetField<T>(UIToggleGroup group, string name, T value)
        {
            typeof(UIToggleGroup)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(group, value);
        }

        private sealed class PendingProcedure : ProcedureBase
        {
            private readonly UniTaskCompletionSource<bool> _completion = new();

            public void Complete() => _completion.TrySetResult(true);

            protected override async UniTask OnExecuteAsync(CancellationToken ct)
            {
                await _completion.Task.AttachExternalCancellation(ct);
            }
        }

        private sealed class FailingProcedure : ProcedureBase
        {
            protected override UniTask OnExecuteAsync(CancellationToken ct)
                => UniTask.FromException(new InvalidOperationException("Preparation failed."));
        }

        private sealed class CompletedProcedure : ProcedureBase
        {
            protected override UniTask OnExecuteAsync(CancellationToken ct)
                => UniTask.CompletedTask;
        }
    }
}
