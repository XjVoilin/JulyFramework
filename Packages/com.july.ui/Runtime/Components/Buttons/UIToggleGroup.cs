using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.UI
{
    public class UIToggleGroup : MonoBehaviour, ICanRunProcedure
    {
        [SerializeField] private int m_SelectedIndex;
        [SerializeField] private List<UIToggleItem> m_Items = new();
        [SerializeField] private List<GameObject> m_Contents = new();

        private Func<int, ProcedureBase> m_ProcedureFactory;
        private CancellationTokenSource m_SelectionRequestCts;

        public event Action<int> OnValueChanged;
        public event Action<int> OnLockedItemClicked;

        public int SelectedIndex => m_SelectedIndex;

        public int Count => m_Items.Count;

        public UIToggleItem GetItem(int index) => m_Items[index];

        /// <summary>
        /// 设置选择前流程工厂。每次有效选择都会调用一次，返回空表示立即切换。
        /// 返回的 Procedure 必须是尚未执行过的新实例。
        /// </summary>
        public void SetProcedureFactory(Func<int, ProcedureBase> factory)
        {
            if (m_ProcedureFactory == factory)
                return;

            CancelPendingSelection();
            m_ProcedureFactory = factory;
        }

        public bool IsItemLocked(int index)
        {
            if (index < 0 || index >= m_Items.Count) return false;
            return m_Items[index] != null && m_Items[index].IsLocked;
        }

        public void SetItemLocked(int index, bool locked)
        {
            if (index < 0 || index >= m_Items.Count) return;
            if (m_Items[index] != null)
                m_Items[index].SetLocked(locked);
        }

        public void SetWithoutNotify(int index)
        {
            if (index < 0 || index >= m_Items.Count) return;
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
                var procedure = m_ProcedureFactory?.Invoke(index);
                if (procedure != null)
                    await this.RunProcedure(procedure, token);

                if (token.IsCancellationRequested) return;

                ApplySelection(index);
                OnValueChanged?.Invoke(m_SelectedIndex);
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
            var index = m_Items.IndexOf(item);
            SelectAsync(index).Forget(Debug.LogException);
        }

        internal void NotifyLockedItemClicked(UIToggleItem item)
        {
            var index = m_Items.IndexOf(item);
            if (index < 0) return;
            OnLockedItemClicked?.Invoke(index);
        }

        private void ApplySelection(int index)
        {
            ValidateConfiguration();

            for (var i = 0; i < m_Items.Count; i++)
                m_Items[i].SetOn(i == index);

            for (var i = 0; i < m_Contents.Count; i++)
                m_Contents[i].SetActive(i == index);

            m_SelectedIndex = index;
        }

        private CancellationTokenSource BeginSelectionRequest()
        {
            CancelPendingSelection();

            m_SelectionRequestCts = new CancellationTokenSource();
            return m_SelectionRequestCts;
        }

        private void CompleteSelectionRequest(CancellationTokenSource cancellation)
        {
            if (!ReferenceEquals(m_SelectionRequestCts, cancellation)) return;

            m_SelectionRequestCts = null;
            cancellation.Dispose();
        }

        private void CancelPendingSelection()
        {
            var cancellation = m_SelectionRequestCts;
            m_SelectionRequestCts = null;
            if (cancellation == null) return;

            cancellation.Cancel();
            cancellation.Dispose();
        }

        private bool CanSelect(int index)
        {
            if (index < 0 || index >= m_Items.Count) return false;
            if (m_SelectedIndex == index) return false;
            return m_Items[index] == null || !m_Items[index].IsLocked;
        }

        private void ValidateConfiguration()
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (m_Items[i] == null)
                    throw new InvalidOperationException($"UIToggleGroup 第 {i} 个 Item 未配置。");
            }

            if (m_Contents.Count == 0)
                return;

            if (m_Contents.Count != m_Items.Count)
            {
                throw new InvalidOperationException(
                    $"UIToggleGroup 有 {m_Items.Count} 个 Item，但配置了 {m_Contents.Count} 个 Content。");
            }

            for (int i = 0; i < m_Contents.Count; i++)
            {
                if (m_Contents[i] == null)
                    throw new InvalidOperationException($"UIToggleGroup 第 {i} 个 Content 未配置。");
            }
        }

        private void OnEnable()
        {
            ValidateConfiguration();
            if (m_Items.Count == 0)
                return;

            m_SelectedIndex = Mathf.Clamp(m_SelectedIndex, 0, m_Items.Count - 1);
            ApplySelection(m_SelectedIndex);
        }

        private void OnDisable()
        {
            CancelPendingSelection();
        }
    }
}
