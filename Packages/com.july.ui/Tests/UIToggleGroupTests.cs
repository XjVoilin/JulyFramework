using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using July.Arch;
using NUnit.Framework;
using UnityEngine;

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

        [Test]
        public void ManualCommit_ClickRequestsSelectionWithoutChangingVisibleContent()
        {
            var group = CreateGroup(out var items);
            var contents = CreateContents();
            SetField(group, "m_Contents", contents);
            SetField(group, "m_SelectionMode", UIToggleSelectionMode.ManualCommit);
            group.SetWithoutNotify(0);

            var requestedIndex = -1;
            group.OnSelectionRequested += index => requestedIndex = index;

            group.NotifyItemClicked(items[1]);

            Assert.That(requestedIndex, Is.EqualTo(1));
            Assert.That(group.SelectedIndex, Is.Zero);
            Assert.That(items[0].IsOn, Is.True);
            Assert.That(items[1].IsOn, Is.False);
            Assert.That(contents[0].activeSelf, Is.True);
            Assert.That(contents[1].activeSelf, Is.False);
        }

        [Test]
        public void CommitSelection_AfterManualRequestChangesContentAndNotifies()
        {
            var group = CreateGroup(out var items);
            var contents = CreateContents();
            SetField(group, "m_Contents", contents);
            SetField(group, "m_SelectionMode", UIToggleSelectionMode.ManualCommit);
            group.SetWithoutNotify(0);

            var changedIndex = -1;
            group.OnValueChanged += index => changedIndex = index;

            group.NotifyItemClicked(items[1]);
            var committed = group.CommitSelection(1);

            Assert.That(committed, Is.True);
            Assert.That(changedIndex, Is.EqualTo(1));
            Assert.That(group.SelectedIndex, Is.EqualTo(1));
            Assert.That(items[0].IsOn, Is.False);
            Assert.That(items[1].IsOn, Is.True);
            Assert.That(contents[0].activeSelf, Is.False);
            Assert.That(contents[1].activeSelf, Is.True);
        }

        [Test]
        public void ImmediateMode_ClickRequestsBeforeCommittingSelection()
        {
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            var events = new List<string>();
            group.OnSelectionRequested += index => events.Add($"request:{index}");
            group.OnValueChanged += index => events.Add($"changed:{index}");

            group.NotifyItemClicked(items[1]);

            Assert.That(events, Is.EqualTo(new[] { "request:1", "changed:1" }));
            Assert.That(group.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public async Task CommitSelectionAsync_WaitsForProcedureBeforeCommitting()
        {
            InitializeArchitecture();
            var group = CreateGroup(out var items);
            group.SetWithoutNotify(0);
            var procedure = new PendingProcedure();

            var selection = group.CommitSelectionAsync(1, procedure);

            Assert.That(group.SelectedIndex, Is.Zero);
            Assert.That(items[0].IsOn, Is.True);
            Assert.That(items[1].IsOn, Is.False);

            procedure.Complete();

            Assert.That(await selection, Is.True);
            Assert.That(group.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public async Task CommitSelectionAsync_WithoutProcedureCommitsImmediately()
        {
            var group = CreateGroup(out _);
            group.SetWithoutNotify(0);

            var committed = await group.CommitSelectionAsync(1);

            Assert.That(committed, Is.True);
            Assert.That(group.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public async Task CommitSelectionAsync_NewerRequestSupersedesPendingProcedure()
        {
            InitializeArchitecture();
            var group = CreateGroup(out _, 3);
            group.SetWithoutNotify(0);
            var pending = new PendingProcedure();

            var firstSelection = group.CommitSelectionAsync(1, pending);
            var secondCommitted = await group.CommitSelectionAsync(2);

            Assert.That(secondCommitted, Is.True);
            Assert.That(await firstSelection, Is.False);
            Assert.That(group.SelectedIndex, Is.EqualTo(2));
        }

        [Test]
        public async Task SetWithoutNotify_CancelsPendingProcedureSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out _);
            group.SetWithoutNotify(0);
            var pending = new PendingProcedure();

            var selection = group.CommitSelectionAsync(1, pending);
            group.SetWithoutNotify(0);

            Assert.That(await selection, Is.False);
            Assert.That(group.SelectedIndex, Is.Zero);
        }

        [Test]
        public async Task CommitSelection_CancelsPendingProcedureSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out _, 3);
            group.SetWithoutNotify(0);
            var pending = new PendingProcedure();

            var selection = group.CommitSelectionAsync(1, pending);
            var committed = group.CommitSelection(2);

            Assert.That(committed, Is.True);
            Assert.That(await selection, Is.False);
            Assert.That(group.SelectedIndex, Is.EqualTo(2));
        }

        [Test]
        public void CommitSelectionAsync_ProcedureFailureKeepsCurrentSelection()
        {
            InitializeArchitecture();
            var group = CreateGroup(out _);
            group.SetWithoutNotify(0);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await group.CommitSelectionAsync(1, new FailingProcedure()));
            Assert.That(group.SelectedIndex, Is.Zero);
        }

        private UIToggleGroup CreateGroup(out List<UIToggleItem> items, int itemCount = 2)
        {
            _root = new GameObject("UIToggleGroup");
            var group = _root.AddComponent<UIToggleGroup>();
            items = new List<UIToggleItem>(itemCount);
            for (var i = 0; i < itemCount; i++)
                items.Add(CreateItem($"Item{i}"));
            SetField(group, "m_Items", items);
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
    }
}
