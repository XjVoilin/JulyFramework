using System;
using System.Collections.Generic;

namespace July.Experiments
{
    /// <summary>
    /// 实验分组配置
    /// </summary>
    [Serializable]
    public class ExperimentGroupConfig
    {
        public string GroupId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Weight { get; set; } = 50;
        public bool IsControl { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public List<string> WhitelistUserIds { get; set; }

        public ExperimentGroup ToGroup()
        {
            return new ExperimentGroup
            {
                GroupId = GroupId,
                Name = Name,
                Description = Description,
                Weight = Weight,
                IsControl = IsControl,
                Parameters = Parameters != null
                    ? new Dictionary<string, object>(Parameters)
                    : new Dictionary<string, object>(),
                WhitelistUserIds = WhitelistUserIds != null
                    ? new List<string>(WhitelistUserIds)
                    : new List<string>()
            };
        }
    }

    /// <summary>
    /// 实验指标配置
    /// </summary>
    [Serializable]
    public class ExperimentMetricConfig
    {
        public string MetricId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsPrimary { get; set; }
        public string Direction { get; set; } = "higher_is_better";

        public ExperimentMetric ToMetric()
        {
            return new ExperimentMetric
            {
                MetricId = MetricId,
                Name = Name,
                Type = Type,
                IsPrimary = IsPrimary,
                Direction = Direction
            };
        }
    }

    /// <summary>
    /// 进入条件配置
    /// </summary>
    [Serializable]
    public class EntryConditionConfig
    {
        public string Type { get; set; }
        public string Param { get; set; }
        public string Operator { get; set; }
        public object Value { get; set; }

        public EntryCondition ToCondition()
        {
            return new EntryCondition
            {
                Type = Type,
                Param = Param,
                Operator = Operator,
                Value = Value
            };
        }
    }

    /// <summary>
    /// 实验配置
    /// </summary>
    [Serializable]
    public class ExperimentConfig
    {
        public string ExperimentId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ExperimentStatus Status { get; set; } = ExperimentStatus.Draft;
        public AllocationStrategy Strategy { get; set; } = AllocationStrategy.UserIdHash;
        public List<ExperimentGroupConfig> Groups { get; set; }
        public List<ExperimentMetricConfig> Metrics { get; set; }
        public List<EntryConditionConfig> EntryConditions { get; set; }
        public List<string> MutualExclusionIds { get; set; }
        public int TrafficPercentage { get; set; } = 100;
        public long? StartTimeUnix { get; set; }
        public long? EndTimeUnix { get; set; }
        public int Priority { get; set; }
        public string Layer { get; set; }
        public Dictionary<string, string> Tags { get; set; }

        public Experiment ToExperiment()
        {
            var experiment = new Experiment
            {
                ExperimentId = ExperimentId,
                Name = Name,
                Description = Description,
                Status = Status,
                Strategy = Strategy,
                TrafficPercentage = TrafficPercentage,
                Priority = Priority,
                Layer = Layer,
                MutualExclusionIds = MutualExclusionIds != null
                    ? new List<string>(MutualExclusionIds)
                    : new List<string>(),
                Tags = Tags != null
                    ? new Dictionary<string, string>(Tags)
                    : new Dictionary<string, string>()
            };

            if (StartTimeUnix.HasValue)
                experiment.StartTime = DateTimeOffset.FromUnixTimeSeconds(StartTimeUnix.Value).UtcDateTime;

            if (EndTimeUnix.HasValue)
                experiment.EndTime = DateTimeOffset.FromUnixTimeSeconds(EndTimeUnix.Value).UtcDateTime;

            if (Groups != null)
            {
                experiment.Groups = new List<ExperimentGroup>();
                foreach (var config in Groups)
                {
                    experiment.Groups.Add(config.ToGroup());
                }
            }

            if (Metrics != null)
            {
                experiment.Metrics = new List<ExperimentMetric>();
                foreach (var config in Metrics)
                {
                    experiment.Metrics.Add(config.ToMetric());
                }
            }

            if (EntryConditions != null)
            {
                experiment.EntryConditions = new List<EntryCondition>();
                foreach (var config in EntryConditions)
                {
                    experiment.EntryConditions.Add(config.ToCondition());
                }
            }

            return experiment;
        }
    }

    /// <summary>
    /// 实验配置表
    /// </summary>
    [Serializable]
    public class ExperimentConfigTable
    {
        public List<ExperimentConfig> Experiments { get; set; } = new List<ExperimentConfig>();

        public List<Experiment> ToExperimentList()
        {
            var result = new List<Experiment>();
            if (Experiments != null)
            {
                foreach (var config in Experiments)
                {
                    result.Add(config.ToExperiment());
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 实验配置构建器
    /// </summary>
    public class ExperimentBuilder
    {
        private readonly Experiment _experiment;

        public ExperimentBuilder(string experimentId, string name)
        {
            _experiment = new Experiment
            {
                ExperimentId = experimentId,
                Name = name,
                Groups = new List<ExperimentGroup>(),
                Metrics = new List<ExperimentMetric>(),
                EntryConditions = new List<EntryCondition>(),
                MutualExclusionIds = new List<string>(),
                Tags = new Dictionary<string, string>()
            };
        }

        public ExperimentBuilder WithDescription(string description)
        {
            _experiment.Description = description;
            return this;
        }

        public ExperimentBuilder WithStrategy(AllocationStrategy strategy)
        {
            _experiment.Strategy = strategy;
            return this;
        }

        public ExperimentBuilder WithTraffic(int percentage)
        {
            _experiment.TrafficPercentage = Math.Max(0, Math.Min(100, percentage));
            return this;
        }

        public ExperimentBuilder WithPriority(int priority)
        {
            _experiment.Priority = priority;
            return this;
        }

        public ExperimentBuilder WithLayer(string layer)
        {
            _experiment.Layer = layer;
            return this;
        }

        public ExperimentBuilder WithPeriod(DateTime? start, DateTime? end)
        {
            _experiment.StartTime = start;
            _experiment.EndTime = end;
            return this;
        }

        public ExperimentBuilder AddControlGroup(string groupId, string name, int weight = 50,
            Dictionary<string, object> parameters = null)
        {
            return AddGroup(groupId, name, weight, true, parameters);
        }

        public ExperimentBuilder AddTreatmentGroup(string groupId, string name, int weight = 50,
            Dictionary<string, object> parameters = null)
        {
            return AddGroup(groupId, name, weight, false, parameters);
        }

        public ExperimentBuilder AddGroup(string groupId, string name, int weight = 50,
            bool isControl = false, Dictionary<string, object> parameters = null)
        {
            _experiment.Groups.Add(new ExperimentGroup
            {
                GroupId = groupId,
                Name = name,
                Weight = weight,
                IsControl = isControl,
                Parameters = parameters ?? new Dictionary<string, object>()
            });
            return this;
        }

        public ExperimentBuilder AddMetric(string metricId, string name, string type,
            bool isPrimary = false, string direction = "higher_is_better")
        {
            _experiment.Metrics.Add(new ExperimentMetric
            {
                MetricId = metricId,
                Name = name,
                Type = type,
                IsPrimary = isPrimary,
                Direction = direction
            });
            return this;
        }

        public ExperimentBuilder AddEntryCondition(string type, string param, string op, object value)
        {
            _experiment.EntryConditions.Add(new EntryCondition
            {
                Type = type,
                Param = param,
                Operator = op,
                Value = value
            });
            return this;
        }

        public ExperimentBuilder ExcludeWith(params string[] experimentIds)
        {
            _experiment.MutualExclusionIds.AddRange(experimentIds);
            return this;
        }

        public ExperimentBuilder WithTag(string key, string value)
        {
            _experiment.Tags[key] = value;
            return this;
        }

        public ExperimentBuilder SetStatus(ExperimentStatus status)
        {
            _experiment.Status = status;
            return this;
        }

        public Experiment Build()
        {
            // 验证
            if (string.IsNullOrEmpty(_experiment.ExperimentId))
                throw new InvalidOperationException("实验ID不能为空");

            if (_experiment.Groups == null || _experiment.Groups.Count == 0)
                throw new InvalidOperationException("实验至少需要一个分组");

            return _experiment;
        }
    }
}

