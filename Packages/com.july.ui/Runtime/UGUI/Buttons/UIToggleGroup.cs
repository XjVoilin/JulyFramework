using System;
using System.Collections.Generic;
using UnityEngine;

namespace July.UI
{
    public enum UIToggleSelectionMode
    {
        Immediate,
        ManualCommit
    }

    public class UIToggleGroup : MonoBehaviour
    {
        [SerializeField] private int m_SelectedIndex;
        [SerializeField] private UIToggleSelectionMode m_SelectionMode;
        [SerializeField] private List<UIToggleItem> m_Items = new();
        [SerializeField] private List<GameObject> m_Contents = new();

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
            ApplySelection(index);
        }

        public bool CommitSelection(int index)
        {
            if (!CanCommitSelection(index)) return false;

            ApplySelection(index);
            OnValueChanged?.Invoke(m_SelectedIndex);
            return true;
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
    }
}
