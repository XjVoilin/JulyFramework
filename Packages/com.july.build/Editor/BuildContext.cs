using System;
using System.Collections.Generic;
using UnityEditor;

namespace July.Build
{
    /// <summary>
    /// Shared build state passed through a pipeline. Projects can derive from this type to add
    /// domain-specific inputs and outputs while still using the framework runner.
    /// </summary>
    public class BuildContext
    {
        private readonly Dictionary<string, object> _values = new();

        public BuildTarget Target { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool Interactive { get; set; } = true;

        public BuildContext()
        {
        }

        public BuildContext(BuildTarget target, string platform, string environment,
            string version, bool interactive = true)
        {
            Target = target;
            Platform = platform ?? string.Empty;
            Environment = environment ?? string.Empty;
            Version = version ?? string.Empty;
            Interactive = interactive;
        }

        /// <summary>Returns null when the shared context is valid.</summary>
        public virtual string Validate() => null;

        public BuildContext Set<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Build context key cannot be empty.", nameof(key));

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

            throw new KeyNotFoundException(
                $"Build context does not contain key '{key}' with value type {typeof(T).Name}.");
        }
    }
}
