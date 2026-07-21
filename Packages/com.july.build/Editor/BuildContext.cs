using System;
using System.Collections.Generic;
using UnityEditor;

namespace July.Build
{
    public sealed class BuildContext
    {
        private readonly Dictionary<string, object> _values = new();

        public BuildTarget Target { get; }
        public string Platform { get; }
        public string Environment { get; }
        public string Version { get; }
        public bool Interactive { get; }

        public BuildContext(BuildTarget target, string platform, string environment,
            string version, bool interactive = true)
        {
            Target = target;
            Platform = platform ?? string.Empty;
            Environment = environment ?? string.Empty;
            Version = version ?? string.Empty;
            Interactive = interactive;
        }

        public BuildContext Set<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("构建上下文键不能为空。", nameof(key));

            _values[key] = value;
            return this;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public T GetRequired<T>(string key)
        {
            if (TryGet<T>(key, out var value))
                return value;

            throw new KeyNotFoundException($"构建上下文缺少键 '{key}' 或值类型不匹配。");
        }
    }
}
