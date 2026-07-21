using System.Collections.Generic;
using NUnit.Framework;

namespace July.Persistence.Tests
{
    public class KeyValueSaveSystemTests
    {
        [Test]
        public void GetSavePath_AppliesInjectedPrefix()
        {
            var system = new KeyValueSaveSystem(new MemoryStore(), "profile/");
            Assert.That(system.GetSavePath("user"), Is.EqualTo("profile/user"));
        }

        [Test]
        public void GetSavePath_RejectsBlankKey()
        {
            var system = new KeyValueSaveSystem(new MemoryStore());
            Assert.That(() => system.GetSavePath(" "), Throws.ArgumentException);
        }

        private sealed class MemoryStore : IKeyValueStore
        {
            private readonly Dictionary<string, string> _values = new();
            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;
            public void SetString(string key, string value) => _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }
    }
}
