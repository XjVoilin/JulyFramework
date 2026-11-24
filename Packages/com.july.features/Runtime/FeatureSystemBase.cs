using System;
using System.Collections.Generic;
using System.Globalization;
using July.Arch;
using July.Persistence;
using July.Platform;

namespace July.Features
{
    public abstract class FeatureSystemBase : SystemBase, IFeatureSystem
    {
        private Dictionary<int, IFeatureRule> _rules;
        private Dictionary<int, FeatureConfig> _features;

        /// <summary>首次加载时收集项目规则。规则实例按 ID 复用。</summary>
        protected virtual IEnumerable<IFeatureRule> CreateProjectRules() => Array.Empty<IFeatureRule>();

        public void LoadConfig(string json)
        {
            var serializer = GetSystem<ISerializeSystem>();
            var rows = (List<FeatureConfig>)serializer.DeserializeFromJson(json, typeof(List<FeatureConfig>))
                       ?? throw new FormatException("功能配置必须是 JSON 数组。");

            _rules ??= CreateRules();
            var features = new Dictionary<int, FeatureConfig>(rows.Count);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row == null)
                    throw new FormatException($"功能配置第 {index + 1} 项不能为空。");
                if (row.id == -1)
                    throw new FormatException($"功能配置第 {index + 1} 项必须填写 id，-1 为保留值。");

                if (row.conditions == null)
                    throw new FormatException($"功能 {row.id} 必须填写 conditions。无条件限制时填写空数组。");

                if (!features.TryAdd(row.id, row))
                    throw new FormatException($"功能配置包含重复 ID {row.id}。");
            }

            _features = features;
        }

        public bool CanUse(int featureId)
        {
            if (_features == null)
                throw new InvalidOperationException("查询功能状态前必须成功加载功能配置。");

            if (!_features.TryGetValue(featureId, out var feature)) return false;

            var satisfied = AreSatisfied(feature.conditions);
            return feature.negate ? !satisfied : satisfied;
        }

        private Dictionary<int, IFeatureRule> CreateRules()
        {
            var platformRule = new PlatformRule(GetSystem<IPlatformSystem>());
            var rules = new Dictionary<int, IFeatureRule>
            {
                [platformRule.Id] = platformRule
            };
            foreach (var rule in CreateProjectRules())
            {
                if (rule == null)
                    throw new ArgumentException("项目功能规则不能为空。");
                if (rule.Id == -1)
                    throw new ArgumentException("功能规则 ID -1 为保留值。");
                if (!rules.TryAdd(rule.Id, rule))
                    throw new ArgumentException($"功能规则 ID {rule.Id} 已注册。");
            }

            return rules;
        }

        private bool AreSatisfied(List<FeatureConditionConfig> conditions)
        {
            foreach (var condition in conditions)
            {
                if (condition == null)
                    throw new FormatException("功能条件不能为空。");
                if (condition.type == -1)
                    throw new FormatException("功能条件必须填写 type，-1 为保留值。");

                var rule = _rules[condition.type];
                if (!rule.IsSatisfied(condition.arg)) return false;
            }

            return true;
        }
    }
}
