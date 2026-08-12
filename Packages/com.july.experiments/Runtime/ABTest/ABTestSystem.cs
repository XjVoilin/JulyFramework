using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;

namespace July.Experiments
{
    public delegate bool ConditionChecker(EntryCondition condition, string userId, Dictionary<string, string> context);
    public delegate string CustomAllocator(Experiment experiment, string userId);

    public abstract class ABTestSystemBase : SystemBase
    {
        private ABTestRepository _repo;
        private readonly Dictionary<string, ConditionChecker> _conditionCheckers = new();
        private CustomAllocator _customAllocator;
        private readonly object _lock = new();
        private readonly Random _random = new();

        protected sealed override UniTask OnInitializeAsync()
        {
            _repo = ResolveRepository();
            RegisterDefaultConditionCheckers();
            OnConfigure();
            return UniTask.CompletedTask;
        }

        protected abstract ABTestRepository ResolveRepository();

        protected sealed override void OnShutdown()
        {
            OnDispose();

            lock (_lock)
                _conditionCheckers.Clear();

            _customAllocator = null;
        }

        protected abstract void OnConfigure();
        protected virtual void OnDispose() { }

        #region User settings

        public void SetUserId(string userId) => _repo.SetUserId(userId);
        public void SetDeviceId(string deviceId) => _repo.SetDeviceId(deviceId);

        public void SetUserAttribute(string key, string value) => _repo.SetUserAttribute(key, value);

        public void SetUserAttributes(Dictionary<string, string> attributes) =>
            _repo.SetUserAttributes(attributes);

        public void ClearUserAttributes() => _repo.ClearUserAttributes();

        #endregion

        #region Experiment management

        public void RegisterExperiment(Experiment experiment) => _repo.StoreExperiment(experiment);

        public void RegisterExperiments(IEnumerable<Experiment> experiments) =>
            _repo.StoreExperiments(experiments);

        public void UnregisterExperiment(string experimentId) => _repo.RemoveExperiment(experimentId);
        public void ClearAllExperiments() => _repo.ClearExperiments();
        public Experiment GetExperiment(string experimentId) => _repo.GetExperiment(experimentId);
        public List<Experiment> GetAllExperiments() => _repo.GetAllExperiments();
        public List<Experiment> GetRunningExperiments() => _repo.GetRunningExperiments();

        public void SetExperimentStatus(string experimentId, ExperimentStatus status)
        {
            var experiment = _repo.GetExperiment(experimentId);
            if (experiment == null) return;

            var oldStatus = experiment.Status;
            experiment.Status = status;
            _repo.UpdateExperiment(experiment);

            Publish(new ExperimentStatusChangedEvent
            {
                ExperimentId = experimentId,
                OldStatus = oldStatus,
                NewStatus = status,
                Experiment = experiment
            });
        }

        public void LoadFromConfigTable(ExperimentConfigTable configTable)
        {
            if (configTable?.Experiments == null) return;
            _repo.StoreExperiments(configTable.ToExperimentList());
        }

        #endregion

        #region Group allocation

        public ExperimentGroup GetUserGroup(string experimentId, string userId = null)
        {
            userId = userId ?? _repo.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return null;

            var experiment = _repo.GetExperiment(experimentId);
            if (experiment == null || !experiment.IsAvailable())
                return null;

            var existingAssignment = _repo.GetAssignment(experimentId);
            if (existingAssignment != null)
            {
                var existingGroup = experiment.Groups.Find(g => g.GroupId == existingAssignment.GroupId);
                if (existingGroup != null)
                    return existingGroup;
            }

            if (!CheckEntryConditions(experiment, userId))
                return null;

            if (!CheckMutualExclusion(experiment, userId))
                return null;

            if (!IsInTraffic(experiment, userId))
                return null;

            var assignedGroup = AllocateGroup(experiment, userId);
            if (assignedGroup == null)
                return null;

            var assignment = new UserExperimentAssignment
            {
                UserId = userId,
                ExperimentId = experimentId,
                GroupId = assignedGroup.GroupId,
                AssignedTime = DateTime.UtcNow,
                ExperimentVersion = 1
            };
            _repo.StoreAssignment(assignment);

            Publish(new UserAssignedToExperimentEvent
            {
                UserId = userId,
                ExperimentId = experimentId,
                GroupId = assignedGroup.GroupId,
                Experiment = experiment,
                Group = assignedGroup,
                IsNewAssignment = true
            });

            return assignedGroup;
        }

        public T GetParameter<T>(string experimentId, string paramKey, T defaultValue = default)
        {
            var group = GetUserGroup(experimentId);
            if (group?.Parameters == null || !group.Parameters.TryGetValue(paramKey, out var value))
                return defaultValue;

            try
            {
                if (value is T typedValue)
                    return typedValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        public bool IsInGroup(string experimentId, string groupId, string userId = null)
        {
            var group = GetUserGroup(experimentId, userId);
            return group != null && group.GroupId == groupId;
        }

        public bool IsInControlGroup(string experimentId, string userId = null)
        {
            var group = GetUserGroup(experimentId, userId);
            return group != null && group.IsControl;
        }

        public bool IsInTreatmentGroup(string experimentId, string userId = null)
        {
            var group = GetUserGroup(experimentId, userId);
            return group != null && !group.IsControl;
        }

        #endregion

        #region Exposure tracking

        public void RecordExposure(string experimentId, string scene = null,
            Dictionary<string, object> extraData = null, string userId = null)
        {
            userId = userId ?? _repo.GetUserId();
            if (string.IsNullOrEmpty(userId)) return;

            var assignment = _repo.GetAssignment(experimentId);
            if (assignment == null) return;

            var isFirstExposure = !assignment.FirstExposureTime.HasValue;
            if (isFirstExposure)
            {
                assignment.FirstExposureTime = DateTime.UtcNow;
                _repo.StoreAssignment(assignment);
            }

            Publish(new ExperimentExposureEvent
            {
                UserId = userId,
                ExperimentId = experimentId,
                GroupId = assignment.GroupId,
                Scene = scene,
                IsFirstExposure = isFirstExposure,
                ExtraData = extraData
            });
        }

        #endregion

        #region Condition checkers

        public void RegisterConditionChecker(string conditionType, ConditionChecker checker)
        {
            lock (_lock)
                _conditionCheckers[conditionType] = checker;
        }

        public void SetCustomAllocator(CustomAllocator allocator) => _customAllocator = allocator;

        private void RegisterDefaultConditionCheckers()
        {
            RegisterConditionChecker("user_attribute", (condition, userId, context) =>
            {
                if (!context.TryGetValue(condition.Param, out var value))
                    return false;

                return EvaluateCondition(value, condition.Operator, condition.Value);
            });

            RegisterConditionChecker("user_id", (condition, userId, context) =>
                EvaluateCondition(userId, condition.Operator, condition.Value));

            RegisterConditionChecker("is_new_user", (condition, userId, context) =>
            {
                if (!context.TryGetValue("is_new_user", out var value))
                    return false;

                return EvaluateCondition(value, condition.Operator, condition.Value);
            });
        }

        private bool EvaluateCondition(object actualValue, string op, object targetValue)
        {
            if (actualValue == null) return false;

            try
            {
                switch (op)
                {
                    case "eq":
                        return actualValue.ToString() == targetValue?.ToString();
                    case "ne":
                        return actualValue.ToString() != targetValue?.ToString();
                    case "gt":
                        return Convert.ToDouble(actualValue) > Convert.ToDouble(targetValue);
                    case "gte":
                        return Convert.ToDouble(actualValue) >= Convert.ToDouble(targetValue);
                    case "lt":
                        return Convert.ToDouble(actualValue) < Convert.ToDouble(targetValue);
                    case "lte":
                        return Convert.ToDouble(actualValue) <= Convert.ToDouble(targetValue);
                    case "contains":
                        return actualValue.ToString().Contains(targetValue?.ToString() ?? "");
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Allocation logic

        private bool CheckEntryConditions(Experiment experiment, string userId)
        {
            if (experiment.EntryConditions == null || experiment.EntryConditions.Count == 0)
                return true;

            var context = new Dictionary<string, string>(_repo.GetUserAttributes());

            foreach (var condition in experiment.EntryConditions)
            {
                ConditionChecker checker;
                lock (_lock)
                {
                    if (!_conditionCheckers.TryGetValue(condition.Type, out checker))
                        return false;
                }

                if (!checker(condition, userId, context))
                    return false;
            }

            return true;
        }

        private bool CheckMutualExclusion(Experiment experiment, string userId)
        {
            if (experiment.MutualExclusionIds == null || experiment.MutualExclusionIds.Count == 0)
                return true;

            foreach (var excludeId in experiment.MutualExclusionIds)
            {
                if (_repo.GetAssignment(excludeId) != null)
                    return false;
            }

            return true;
        }

        private bool IsInTraffic(Experiment experiment, string userId)
        {
            if (experiment.TrafficPercentage >= 100) return true;
            if (experiment.TrafficPercentage <= 0) return false;

            var hash = GetDeterministicHash(userId + "_traffic_" + experiment.ExperimentId);
            return Math.Abs(hash % 100) < experiment.TrafficPercentage;
        }

        private ExperimentGroup AllocateGroup(Experiment experiment, string userId)
        {
            if (experiment.Groups == null || experiment.Groups.Count == 0)
                return null;

            foreach (var group in experiment.Groups)
            {
                if (group.WhitelistUserIds != null && group.WhitelistUserIds.Contains(userId))
                    return group;
            }

            switch (experiment.Strategy)
            {
                case AllocationStrategy.Random:
                    return AllocateByRandom(experiment);
                case AllocationStrategy.DeviceIdHash:
                    return AllocateByHash(experiment, _repo.GetDeviceId() ?? userId);
                case AllocationStrategy.Custom when _customAllocator != null:
                    var groupId = _customAllocator(experiment, userId);
                    return experiment.Groups.Find(g => g.GroupId == groupId);
                default:
                    return AllocateByHash(experiment, userId);
            }
        }

        private ExperimentGroup AllocateByRandom(Experiment experiment)
        {
            var totalWeight = experiment.GetTotalWeight();
            if (totalWeight <= 0) return experiment.Groups[0];

            var randomValue = _random.Next(totalWeight);
            var cumulative = 0;
            foreach (var group in experiment.Groups)
            {
                cumulative += group.Weight;
                if (randomValue < cumulative)
                    return group;
            }

            return experiment.Groups[experiment.Groups.Count - 1];
        }

        private ExperimentGroup AllocateByHash(Experiment experiment, string hashKey)
        {
            var totalWeight = experiment.GetTotalWeight();
            if (totalWeight <= 0) return experiment.Groups[0];

            var bucket = Math.Abs(GetDeterministicHash(hashKey + "_" + experiment.ExperimentId) % totalWeight);
            var cumulative = 0;
            foreach (var group in experiment.Groups)
            {
                cumulative += group.Weight;
                if (bucket < cumulative)
                    return group;
            }

            return experiment.Groups[experiment.Groups.Count - 1];
        }

        private static int GetDeterministicHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;

            unchecked
            {
                var hash = 5381;
                foreach (var c in input)
                    hash = ((hash << 5) + hash) + c;
                return hash;
            }
        }

        #endregion

        public void ClearUserData()
        {
            _repo.ClearAssignments();
        }
    }
}
