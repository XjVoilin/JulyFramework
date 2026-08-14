using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.UI
{
    public enum UIToggleSelectionMode
    {
        Immediate,
        ManualCommit
    }

    public class UIToggleGroup : MonoBehaviour, ICanRunProcedure
    {
        [SerializeField] private int m_SelectedIndex;
        [SerializeField] private UIToggleSelectionMode m_SelectionMode;
        [SerializeField] private List<UIToggleItem> m_Items = new();
        [SerializeField] private List<GameObject> m_Contents = new();

        private CancellationTokenSource m_SelectionRequestCts;

        public event Action<int> OnSelectionRequested;
        public event Action<int> OnValueChanged;
        public event Action<int> OnLockedItemClicked;

        public int SelectedIndex
        {
            get => m_SelectedIndex;
            set => CommitSelection(value);
        }

        public int Count => m_Items.Count;

        public UIToggleItem GetItem(int index) => m_Items[index];

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

        public bool CommitSelection(int index)
        {
            if (!CanCommitSelection(index)) return false;

            CancelPendingSelection();
            return CommitSelectionCore(index);
        }

        /// <summary>
        /// 提交选择前执行一个一次性的准备流程。
        /// <paramref name="preparation"/> 为空时立即提交；新的选择请求、同步选择或组件禁用
        /// 会取消尚未完成的请求并返回 false。准备流程抛出的异常会继续向上传播，当前选择保持不变。
        /// 通过点击触发时应配合 <see cref="UIToggleSelectionMode.ManualCommit"/> 使用，
        /// 并为每次请求传入新的 Procedure 实例。
        /// </summary>
        public async UniTask<bool> CommitSelectionAsync(
            int index,
            ProcedureBase preparation = null,
            CancellationToken ct = default)
        {
            if (!CanCommitSelection(index)) return false;

            var cancellation = BeginSelectionRequest(ct);
            var token = cancellation.Token;
            try
            {
                if (preparation != null)
                    await this.RunProcedure(preparation, token);

                if (token.IsCancellationRequested) return false;

                return CommitSelectionCore(index);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                CompleteSelectionRequest(cancellation);
            }
        }

        internal void NotifyItemClicked(UIToggleItem item)
        {
            int index = m_Items.IndexOf(item);
            if (!CanCommitSelection(index)) return;

            OnSelectionRequested?.Invoke(index);

            if (m_SelectionMode == UIToggleSelectionMode.Immediate && m_SelectedIndex != index)
                CommitSelection(index);
        }

        internal void NotifyLockedItemClicked(UIToggleItem item)
        {
            int index = m_Items.IndexOf(item);
            if (index < 0) return;
            OnLockedItemClicked?.Invoke(index);
        }

        private void ApplySelection(int index)
        {
            ValidateConfiguration();

            for (int i = 0; i < m_Items.Count; i++)
                m_Items[i].SetOn(i == index);

            for (int i = 0; i < m_Contents.Count; i++)
                m_Contents[i].SetActive(i == index);

            m_SelectedIndex = index;
        }

        private bool CommitSelectionCore(int index)
        {
            if (!CanCommitSelection(index)) return false;

            ApplySelection(index);
            OnValueChanged?.Invoke(m_SelectedIndex);
            return true;
        }

        private CancellationTokenSource BeginSelectionRequest(CancellationToken ct)
        {
            CancelPendingSelection();

            m_SelectionRequestCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
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

        private bool CanCommitSelection(int index)
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
                    throw new InvalidOperationException($"UIToggleGroup item {i} is not assigned.");
            }

            if (m_Contents.Count == 0)
                return;

            if (m_Contents.Count != m_Items.Count)
            {
                throw new InvalidOperationException(
                    $"UIToggleGroup has {m_Items.Count} items but {m_Contents.Count} contents.");
            }

            for (int i = 0; i < m_Contents.Count; i++)
            {
                if (m_Contents[i] == null)
                    throw new InvalidOperationException($"UIToggleGroup content {i} is not assigned.");
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
