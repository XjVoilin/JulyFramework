using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.UI
{
    public class UIToggleGroup : MonoBehaviour, ICanRunProcedure
    {
        [FormerlySerializedAs("m_SelectedIndex")]
        [SerializeField] private int _selectedIndex;
        [FormerlySerializedAs("m_Items")]
        [SerializeField] private List<UIToggleItem> _items = new();
        [FormerlySerializedAs("m_Contents")]
        [SerializeField] private List<GameObject> _contents = new();

        private Func<int, ProcedureBase> _procedureFactory;
        private CancellationTokenSource _selectionRequestCts;

        public event Action<int> OnValueChanged;
        public event Action<int> OnLockedItemClicked;

        public int SelectedIndex => _selectedIndex;

        public int Count => _items.Count;

        public UIToggleItem GetItem(int index) => _items[index];

        /// <summary>
        /// 设置选择前流程工厂。每次有效选择都会调用一次，返回空表示立即切换。
        /// 返回的 Procedure 必须是尚未执行过的新实例。
        /// </summary>
        public void SetProcedureFactory(Func<int, ProcedureBase> factory)
        {
            if (_procedureFactory == factory)
                return;

            CancelPendingSelection();
            _procedureFactory = factory;
        }

        public bool IsItemLocked(int index)
        {
            if (index < 0 || index >= _items.Count) return false;
            return _items[index] != null && _items[index].IsLocked;
        }

        public void SetItemLocked(int index, bool locked)
        {
            if (index < 0 || index >= _items.Count) return;
            if (_items[index] != null)
                _items[index].SetLocked(locked);
        }

        public void SetWithoutNotify(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            CancelPendingSelection();
            ApplySelection(index);
        }

        /// <summary>
        /// 执行当前选择对应的准备流程，成功后提交选择。
        /// 新的选择、强制重置或组件禁用会取消尚未完成的选择。
        /// </summary>
        private async UniTask SelectAsync(int index)
        {
            if (!CanSelect(index)) return;

            var cancellation = BeginSelectionRequest();
            var token = cancellation.Token;
            try
            {
                var procedure = _procedureFactory?.Invoke(index);
                if (procedure != null)
                    await this.RunProcedure(procedure, token);

                if (token.IsCancellationRequested) return;

                ApplySelection(index);
                OnValueChanged?.Invoke(_selectedIndex);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                CompleteSelectionRequest(cancellation);
            }
        }

        internal void NotifyItemClicked(UIToggleItem item)
        {
            var index = _items.IndexOf(item);
            SelectAsync(index).Forget(Debug.LogException);
        }

        internal void NotifyLockedItemClicked(UIToggleItem item)
        {
            var index = _items.IndexOf(item);
            if (index < 0) return;
            OnLockedItemClicked?.Invoke(index);
        }

        private void ApplySelection(int index)
        {
            ValidateConfiguration();

            for (var i = 0; i < _items.Count; i++)
                _items[i].SetOn(i == index);

            for (var i = 0; i < _contents.Count; i++)
                _contents[i].SetActive(i == index);

            _selectedIndex = index;
        }

        private CancellationTokenSource BeginSelectionRequest()
        {
            CancelPendingSelection();

            _selectionRequestCts = new CancellationTokenSource();
            return _selectionRequestCts;
        }

        private void CompleteSelectionRequest(CancellationTokenSource cancellation)
        {
            if (!ReferenceEquals(_selectionRequestCts, cancellation)) return;

            _selectionRequestCts = null;
            cancellation.Dispose();
        }

        private void CancelPendingSelection()
        {
            var cancellation = _selectionRequestCts;
            _selectionRequestCts = null;
            if (cancellation == null) return;

            cancellation.Cancel();
            cancellation.Dispose();
        }

        private bool CanSelect(int index)
        {
            if (index < 0 || index >= _items.Count) return false;
            if (_selectedIndex == index) return false;
            return _items[index] == null || !_items[index].IsLocked;
        }

        private void ValidateConfiguration()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null)
                    throw new InvalidOperationException($"UIToggleGroup 第 {i} 个 Item 未配置。");
            }

            if (_contents.Count == 0)
                return;

            if (_contents.Count != _items.Count)
            {
                throw new InvalidOperationException(
                    $"UIToggleGroup 有 {_items.Count} 个 Item，但配置了 {_contents.Count} 个 Content。");
            }

            for (int i = 0; i < _contents.Count; i++)
            {
                if (_contents[i] == null)
                    throw new InvalidOperationException($"UIToggleGroup 第 {i} 个 Content 未配置。");
            }
        }

        private void OnEnable()
        {
            ValidateConfiguration();
            if (_items.Count == 0)
                return;

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _items.Count - 1);
            ApplySelection(_selectedIndex);
        }

        private void OnDisable()
        {
            CancelPendingSelection();
        }
    }
}
