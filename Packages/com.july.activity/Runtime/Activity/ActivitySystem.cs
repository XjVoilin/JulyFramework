using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Time;

namespace July.Activity
{
    public abstract class ActivitySystemBase : SystemBase, IUpdatableSystem
    {
        private const float StateCheckInterval = 60f;

        private ActivityRepository _repo;
        private readonly HashSet<string> _newlyOpenedIds = new();
        private float _lastStateCheckTime;
        private bool _isReady;

        protected sealed override UniTask OnInitializeAsync()
        {
            _repo = ResolveRepository();
            _isReady = false;
            _lastStateCheckTime = 0f;
            OnConfigure();
            return UniTask.CompletedTask;
        }

        protected sealed override void OnShutdown()
        {
            OnDispose();
            _newlyOpenedIds.Clear();
            _isReady = false;
        }

        protected abstract void OnConfigure();
        protected virtual void OnDispose() { }

        protected abstract ActivityRepository ResolveRepository();

        /// <summary>服务器 UTC 时间戳（秒）。可覆写以接入对时或便于测试。</summary>
        protected virtual long OnGetServerTimeUtc() => GetSystem<ITimeSystem>().ServerTimeSeconds;

        public void OnUpdate(float deltaTime)
        {
            if (!_isReady || _repo.Definitions.Count == 0)
                return;

            _lastStateCheckTime += GetSystem<ITimeSystem>().UnscaledDeltaTime;
            if (_lastStateCheckTime >= StateCheckInterval)
            {
                _lastStateCheckTime = 0f;
                CheckAndUpdateStates();
            }
        }

        public void RegisterActivity(ActivityDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
                return;

            _repo.RegisterDefinition(definition);
            _repo.SetCachedState(
                definition.Id,
                CalculateState(definition.PreAnnounceTime, definition.StartTime, definition.EndTime));
        }

        public void RegisterActivities(IEnumerable<ActivityDefinition> definitions)
        {
            if (definitions == null) return;

            foreach (var definition in definitions)
                RegisterActivity(definition);
        }

        public bool UnregisterActivity(string activityId)
        {
            if (string.IsNullOrEmpty(activityId))
                return false;

            _newlyOpenedIds.Remove(activityId);
            return _repo.UnregisterDefinition(activityId);
        }

        public void CompleteRegistration()
        {
            if (_isReady)
                return;

            ProcessActivityStates();
            _isReady = true;

            Publish(new ActivityRegisteredEvent
            {
                RegisteredCount = _repo.Definitions.Count
            });

            var activeCount = _repo.StateCache.Values.Count(state => state == ActivityState.InProgress);
            Publish(new ActivityModuleReadyEvent
            {
                ActiveCount = activeCount,
                NewlyOpenedCount = _newlyOpenedIds.Count
            });
        }

        public List<ActivityInfo> GetAllActivities()
        {
            var result = new List<ActivityInfo>(_repo.Definitions.Count);
            foreach (var pair in _repo.Definitions)
                result.Add(BuildActivityInfo(pair.Key, pair.Value));

            return result.OrderBy(info => info.Definition.Priority).ToList();
        }

        public ActivityInfo GetActivity(string activityId)
        {
            if (string.IsNullOrEmpty(activityId))
                return null;

            return _repo.TryGetDefinition(activityId, out var definition)
                ? BuildActivityInfo(activityId, definition)
                : null;
        }

        public List<ActivityInfo> GetActivitiesByType(int type)
        {
            return GetAllActivities().Where(info => info.Definition.Type == type).ToList();
        }

        public List<ActivityInfo> GetActivitiesByState(ActivityState state)
        {
            return GetAllActivities().Where(info => info.State == state).ToList();
        }

        public ActivityState GetActivityState(string activityId)
        {
            if (string.IsNullOrEmpty(activityId))
                return ActivityState.NotStarted;

            if (_repo.TryGetCachedState(activityId, out var state))
                return state;

            if (_repo.TryGetDefinition(activityId, out var definition))
            {
                state = CalculateState(definition.PreAnnounceTime, definition.StartTime, definition.EndTime);
                _repo.SetCachedState(activityId, state);
                return state;
            }

            return ActivityState.NotStarted;
        }

        public bool HasActivity(string activityId)
        {
            return !string.IsNullOrEmpty(activityId) && _repo.Definitions.ContainsKey(activityId);
        }

        public ActivityState CalculateState(long preAnnounceTime, long startTime, long endTime)
        {
            var now = OnGetServerTimeUtc();

            if (now > endTime)
                return ActivityState.Ended;

            if (now >= startTime)
                return ActivityState.InProgress;

            if (preAnnounceTime > 0 && preAnnounceTime < startTime && now >= preAnnounceTime)
                return ActivityState.PreAnnounce;

            return ActivityState.NotStarted;
        }

        public ActivityState CalculateState(long startTime, long endTime)
        {
            return CalculateState(0, startTime, endTime);
        }

        public ActivityRecord GetActivityRecord(string activityId)
        {
            return _repo.TryGetRecord(activityId, out var record) ? record : null;
        }

        public void SaveProgressData(string activityId, string dataPayload)
        {
            if (string.IsNullOrEmpty(activityId))
                return;

            var record = _repo.GetOrCreateRecord(activityId);
            record.DataPayload = dataPayload;
            record.LastUpdateTime = OnGetServerTimeUtc();
            _repo.UpdateRecord(record);

            Publish(new ActivityProgressChangedEvent
            {
                ActivityId = activityId,
                Record = record
            });
        }

        public string GetProgressData(string activityId)
        {
            return GetActivityRecord(activityId)?.DataPayload;
        }

        public void ClearActivityData(string activityId)
        {
            _repo.ClearActivityData(activityId);
        }

        private void CheckAndUpdateStates()
        {
            foreach (var pair in _repo.Definitions)
            {
                var activityId = pair.Key;
                var definition = pair.Value;
                var oldState = _repo.TryGetCachedState(activityId, out var cachedState)
                    ? cachedState
                    : ActivityState.NotStarted;
                var newState = CalculateState(definition.PreAnnounceTime, definition.StartTime, definition.EndTime);

                if (oldState == newState)
                    continue;

                _repo.SetCachedState(activityId, newState);
                OnActivityStateChanged(activityId, definition, oldState, newState);
            }
        }

        private void ProcessActivityStates()
        {
            foreach (var pair in _repo.Definitions)
            {
                var activityId = pair.Key;
                var definition = pair.Value;
                var state = _repo.TryGetCachedState(activityId, out var cachedState)
                    ? cachedState
                    : ActivityState.NotStarted;

                if (state != ActivityState.InProgress || _repo.IsActivityOpened(activityId))
                    continue;

                _newlyOpenedIds.Add(activityId);
                _repo.MarkOpened(activityId);
                Publish(new ActivityOpenedEvent
                {
                    ActivityId = activityId,
                    Definition = definition
                });
            }
        }

        private void OnActivityStateChanged(
            string activityId,
            ActivityDefinition definition,
            ActivityState oldState,
            ActivityState newState)
        {
            if (newState == ActivityState.InProgress &&
                (oldState == ActivityState.NotStarted || oldState == ActivityState.PreAnnounce))
            {
                if (!_repo.IsActivityOpened(activityId))
                {
                    _newlyOpenedIds.Add(activityId);
                    _repo.MarkOpened(activityId);
                }

                Publish(new ActivityOpenedEvent
                {
                    ActivityId = activityId,
                    Definition = definition
                });
            }
            else if (newState == ActivityState.Ended)
            {
                _newlyOpenedIds.Remove(activityId);
                Publish(new ActivityClosedEvent
                {
                    ActivityId = activityId,
                    Definition = definition
                });
            }
        }

        private ActivityInfo BuildActivityInfo(string activityId, ActivityDefinition definition)
        {
            return new ActivityInfo
            {
                Definition = definition,
                State = GetActivityState(activityId),
                Record = GetActivityRecord(activityId),
                IsNewlyOpened = _newlyOpenedIds.Contains(activityId)
            };
        }
    }
}
