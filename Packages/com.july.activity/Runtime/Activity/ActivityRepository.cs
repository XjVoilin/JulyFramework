using System;
using System.Collections.Generic;

namespace July.Activity
{
    public sealed class ActivityRepository
    {
        private readonly ActivityRuntimeData _data;
        private readonly Action _markDirty;

        public Dictionary<string, ActivityDefinition> Definitions { get; } = new();
        public Dictionary<string, ActivityState> StateCache { get; } = new();

        public IReadOnlyDictionary<string, ActivityDefinition> ReadOnlyDefinitions => Definitions;
        public IReadOnlyDictionary<string, ActivityState> ReadOnlyStateCache => StateCache;

        public ActivityRepository(ActivityRuntimeData data, Action markDirty)
        {
            _data = data ?? new ActivityRuntimeData();
            _markDirty = markDirty;
        }

        #region Definitions (runtime-only, no markDirty)

        public bool TryGetDefinition(string activityId, out ActivityDefinition definition)
        {
            return Definitions.TryGetValue(activityId, out definition);
        }

        public void RegisterDefinition(ActivityDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return;
            Definitions[definition.Id] = definition;
        }

        public bool UnregisterDefinition(string activityId)
        {
            if (string.IsNullOrEmpty(activityId)) return false;
            var removed = Definitions.Remove(activityId);
            StateCache.Remove(activityId);
            return removed;
        }

        #endregion

        #region Records (persistent, calls markDirty)

        public bool TryGetRecord(string activityId, out ActivityRecord record)
        {
            return _data.RecordMap.TryGetValue(activityId, out record);
        }

        public ActivityRecord GetOrCreateRecord(string activityId)
        {
            if (_data.RecordMap.TryGetValue(activityId, out var record))
                return record;

            record = new ActivityRecord { ActivityId = activityId };
            _data.RecordMap[activityId] = record;
            _markDirty?.Invoke();
            return record;
        }

        public void UpdateRecord(ActivityRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.ActivityId)) return;
            _data.RecordMap[record.ActivityId] = record;
            _markDirty?.Invoke();
        }

        #endregion

        #region OpenedActivityIds (persistent, calls markDirty)

        public bool IsActivityOpened(string activityId)
        {
            return !string.IsNullOrEmpty(activityId) && _data.OpenedActivityIds.Contains(activityId);
        }

        public bool MarkOpened(string activityId)
        {
            if (string.IsNullOrEmpty(activityId)) return false;
            var added = _data.OpenedActivityIds.Add(activityId);
            if (added) _markDirty?.Invoke();
            return added;
        }

        #endregion

        #region StateCache (runtime-only, no markDirty)

        public void SetCachedState(string activityId, ActivityState state)
        {
            if (string.IsNullOrEmpty(activityId)) return;
            StateCache[activityId] = state;
        }

        public bool TryGetCachedState(string activityId, out ActivityState state)
        {
            return StateCache.TryGetValue(activityId, out state);
        }

        #endregion

        #region Cleanup

        public void ClearActivityData(string activityId)
        {
            if (string.IsNullOrEmpty(activityId)) return;

            var changed = _data.RecordMap.Remove(activityId);
            if (_data.OpenedActivityIds.Remove(activityId))
                changed = true;

            if (changed)
                _markDirty?.Invoke();
        }

        #endregion
    }
}
