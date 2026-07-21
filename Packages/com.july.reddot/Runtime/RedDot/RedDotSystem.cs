using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;

namespace July.RedDot
{
    public abstract class RedDotSystemBase : SystemBase
    {
        private RedDotStore _store;
        private RedDotHandler[] _handlers;
        private readonly Dictionary<string, RedDotValueCalculator> _calculators = new();

        protected sealed override UniTask OnInitializeAsync()
        {
            _store = GetStore<RedDotStore>();
            return UniTask.CompletedTask;
        }

        protected sealed override void OnPostInitialize()
        {
            OnRegisterNodes();
            _handlers = OnCreateHandlers();
            if (_handlers != null)
                foreach (var h in _handlers)
                    h.Attach(this);
        }

        protected sealed override void OnShutdown()
        {
            if (_handlers != null)
                foreach (var h in _handlers)
                    h.Detach();
            _handlers = null;
            _calculators.Clear();
        }

        /// <summary>
        /// 注册红点节点树结构（由编辑器生成的 RegisterAll 或手动注册）
        /// </summary>
        protected abstract void OnRegisterNodes();

        /// <summary>
        /// 创建并返回红点计算器绑定列表（可选）
        /// </summary>
        protected virtual RedDotHandler[] OnCreateHandlers() => null;

        #region Node registration

        public bool RegisterNode(string key, string parentKey = null, RedDotType type = RedDotType.Normal)
        {
            return _store.RegisterNode(key, parentKey, type);
        }

        public void RegisterNodes(IEnumerable<(string Key, string ParentKey, RedDotType Type)> nodes)
        {
            _store.RegisterNodes(nodes);
        }

        #endregion

        #region Queries

        public RedDotNode GetNode(string key) => _store.GetNode(key);
        public int GetCount(string key) => _store.GetCount(key);

        #endregion

        #region Enable flags

        public void SetEnabled(string key, bool enabled)
        {
            _store.SetEnabled(key, enabled);
            Publish(new RedDotEnabledChangedEvent { Key = key, Enabled = enabled });
        }

        public bool GetEnabled(string key) => _store.GetEnabled(key);

        public void SetAllEnabled(bool enabled)
        {
            _store.SetGlobalEnabled(enabled);
            Publish(new RedDotEnabledChangedEvent { Key = null, Enabled = enabled });
        }

        public bool GetAllEnabled() => _store.GetGlobalEnabled();

        #endregion

        #region Calculators

        public void SetCalculator(string key, RedDotValueCalculator calculator)
        {
            if (string.IsNullOrEmpty(key) || calculator == null)
                return;

            var node = _store.GetNode(key);
            if (node == null || !node.IsLeaf)
                return;

            _calculators[key] = calculator;
        }

        public void RemoveCalculator(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _calculators.Remove(key);
        }

        public void Refresh(string key)
        {
            PublishChanges(TriggerCalculator(key));
        }

        public void RefreshAll()
        {
            PublishChanges(TriggerAllCalculators());
        }

        #endregion

        #region Count operations

        public void SetCount(string key, int count)
        {
            PublishChanges(_store.SetCount(key, count));
        }

        public void Clear(string key)
        {
            var node = _store.GetNode(key);
            if (node == null) return;

            if (node.IsLeaf)
            {
                PublishChanges(_store.SetCount(key, 0));
                return;
            }

            var leafKeys = GetLeafKeysRecursive(key);
            if (leafKeys.Count == 0) return;

            var counts = new Dictionary<string, int>(leafKeys.Count);
            foreach (var leafKey in leafKeys)
                counts[leafKey] = 0;

            PublishChanges(_store.SetCountBatch(counts));
        }

        public void ClearAll()
        {
            var leafNodes = _store.GetLeafNodes();
            var counts = new Dictionary<string, int>(leafNodes.Count);
            foreach (var leaf in leafNodes)
                counts[leaf.Key] = 0;

            PublishChanges(_store.SetCountBatch(counts));
        }

        #endregion

        #region Event subscription (for MonoBehaviour UI)

        public void OnKeyChanged(string key, Action<RedDotChangedEvent> handler, object target)
        {
            ArchContext.Current.Event.Subscribe<RedDotChangedEvent>(evt =>
            {
                if (evt.Key == key)
                    handler(evt);
            }, target);
        }

        public void OnEnabledChanged(Action<RedDotEnabledChangedEvent> handler, object target)
        {
            ArchContext.Current.Event.Subscribe(handler, target);
        }

        #endregion

        #region Private helpers

        private List<RedDotChangeInfo> TriggerCalculator(string key)
        {
            if (!_calculators.TryGetValue(key, out var calculator))
                return new List<RedDotChangeInfo>();

            try
            {
                return _store.SetCount(key, calculator(key));
            }
            catch
            {
                return new List<RedDotChangeInfo>();
            }
        }

        private List<RedDotChangeInfo> TriggerAllCalculators()
        {
            if (_calculators.Count == 0)
                return new List<RedDotChangeInfo>();

            var counts = new Dictionary<string, int>(_calculators.Count);
            foreach (var kvp in _calculators)
            {
                try
                {
                    counts[kvp.Key] = kvp.Value(kvp.Key);
                }
                catch
                {
                    // skip failed calculator
                }
            }

            return _store.SetCountBatch(counts);
        }

        private void PublishChanges(List<RedDotChangeInfo> changes)
        {
            if (changes == null || changes.Count == 0)
                return;

            foreach (var change in changes)
            {
                Publish(new RedDotChangedEvent
                {
                    Key = change.Key,
                    OldCount = change.OldCount,
                    NewCount = change.NewCount,
                    Type = change.Type
                });
            }
        }

        private List<string> GetLeafKeysRecursive(string key)
        {
            var result = new List<string>();
            var node = _store.GetNode(key);
            if (node == null) return result;

            if (node.IsLeaf)
            {
                result.Add(key);
                return result;
            }

            foreach (var child in _store.GetChildren(key))
                result.AddRange(GetLeafKeysRecursive(child.Key));

            return result;
        }

        #endregion
    }
}
