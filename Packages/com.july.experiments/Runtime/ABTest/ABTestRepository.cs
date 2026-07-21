using System;
using System.Collections.Generic;
using System.Linq;

namespace July.Experiments
{
    public sealed class ABTestRepository
    {
        private readonly ABTestSaveData _data;
        private readonly Action _markDirty;

        public Dictionary<string, Experiment> Experiments { get; } = new();
        public string DeviceId { get; set; }
        public Dictionary<string, string> UserAttributes { get; } = new();

        public ABTestRepository(ABTestSaveData data, Action markDirty)
        {
            _data = data ?? new ABTestSaveData();
            _markDirty = markDirty;
        }

        #region Experiments (runtime-only, no markDirty)

        public void StoreExperiment(Experiment experiment)
        {
            if (experiment == null || string.IsNullOrEmpty(experiment.ExperimentId))
                return;
            Experiments[experiment.ExperimentId] = experiment;
        }

        public void StoreExperiments(IEnumerable<Experiment> experiments)
        {
            if (experiments == null) return;
            foreach (var experiment in experiments)
            {
                if (experiment != null && !string.IsNullOrEmpty(experiment.ExperimentId))
                    Experiments[experiment.ExperimentId] = experiment;
            }
        }

        public bool RemoveExperiment(string experimentId)
        {
            return !string.IsNullOrEmpty(experimentId) && Experiments.Remove(experimentId);
        }

        public void ClearExperiments()
        {
            Experiments.Clear();
        }

        public Experiment GetExperiment(string experimentId)
        {
            if (string.IsNullOrEmpty(experimentId)) return null;
            return Experiments.GetValueOrDefault(experimentId);
        }

        public List<Experiment> GetAllExperiments() => Experiments.Values.ToList();

        public List<Experiment> GetRunningExperiments()
        {
            return Experiments.Values.Where(e => e.Status == ExperimentStatus.Running).ToList();
        }

        public bool UpdateExperiment(Experiment experiment)
        {
            if (experiment == null || string.IsNullOrEmpty(experiment.ExperimentId))
                return false;
            if (!Experiments.ContainsKey(experiment.ExperimentId))
                return false;
            Experiments[experiment.ExperimentId] = experiment;
            return true;
        }

        #endregion

        #region Assignments (persistent, calls markDirty)

        public void StoreAssignment(UserExperimentAssignment assignment)
        {
            if (assignment == null || string.IsNullOrEmpty(assignment.ExperimentId))
                return;
            _data.Assignments[assignment.ExperimentId] = assignment;
            _markDirty?.Invoke();
        }

        public UserExperimentAssignment GetAssignment(string experimentId)
        {
            if (string.IsNullOrEmpty(experimentId)) return null;
            return _data.Assignments.GetValueOrDefault(experimentId);
        }

        public bool RemoveAssignment(string experimentId)
        {
            var removed = !string.IsNullOrEmpty(experimentId) && _data.Assignments.Remove(experimentId);
            if (removed) _markDirty?.Invoke();
            return removed;
        }

        public void ClearAssignments()
        {
            _data.Assignments.Clear();
            _markDirty?.Invoke();
        }

        #endregion

        #region User Identity (UserId persistent, DeviceId/Attributes runtime)

        public string GetUserId() => _data.UserId;

        public void SetUserId(string userId)
        {
            _data.UserId = userId;
            _markDirty?.Invoke();
        }

        public string GetDeviceId() => DeviceId;

        public void SetDeviceId(string deviceId)
        {
            DeviceId = deviceId;
        }

        public void SetUserAttribute(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            UserAttributes[key] = value;
        }

        public void SetUserAttributes(Dictionary<string, string> attributes)
        {
            if (attributes == null) return;
            foreach (var kvp in attributes)
                UserAttributes[kvp.Key] = kvp.Value;
        }

        public void ClearUserAttributes()
        {
            UserAttributes.Clear();
        }

        public IReadOnlyDictionary<string, string> GetUserAttributes() => UserAttributes;

        #endregion
    }
}
