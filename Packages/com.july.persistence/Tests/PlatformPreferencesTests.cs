using System.Collections.Generic;
using NUnit.Framework;

namespace July.Persistence.Tests
{
    public class PlatformPreferencesTests
    {
        private MemoryPreferencesAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            _adapter = new MemoryPreferencesAdapter();
            PlatformPreferences.SetAdapter(_adapter);
        }

        [TearDown]
        public void TearDown() => PlatformPreferences.UseUnityPlayerPrefs();

        [Test]
        public void Operations_AreRoutedThroughSelectedAdapter()
        {
            PlatformPreferences.SetString("name", "goose");
            PlatformPreferences.SetInt("level", 7);
            PlatformPreferences.SetFloat("volume", 0.5f);
            PlatformPreferences.Save();

            Assert.That(PlatformPreferences.GetString("name"), Is.EqualTo("goose"));
            Assert.That(PlatformPreferences.GetInt("level"), Is.EqualTo(7));
            Assert.That(PlatformPreferences.GetFloat("volume"), Is.EqualTo(0.5f));
            Assert.That(PlatformPreferences.HasKey("name"), Is.True);
            Assert.That(_adapter.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void SetAdapter_RejectsNull()
        {
            Assert.That(() => PlatformPreferences.SetAdapter(null),
                Throws.ArgumentNullException);
        }

        private sealed class MemoryPreferencesAdapter : IPlatformPreferencesAdapter
        {
            private readonly Dictionary<string, object> _values = new();
            public int SaveCount { get; private set; }
            public string GetString(string key, string defaultValue = "") =>
                _values.TryGetValue(key, out var value) ? (string)value : defaultValue;
            public void SetString(string key, string value) => _values[key] = value;
            public int GetInt(string key, int defaultValue = 0) =>
                _values.TryGetValue(key, out var value) ? (int)value : defaultValue;
            public void SetInt(string key, int value) => _values[key] = value;
            public float GetFloat(string key, float defaultValue = 0f) =>
                _values.TryGetValue(key, out var value) ? (float)value : defaultValue;
            public void SetFloat(string key, float value) => _values[key] = value;
            public bool HasKey(string key) => _values.ContainsKey(key);
            public void DeleteKey(string key) => _values.Remove(key);
            public void DeleteAll() => _values.Clear();
            public void Save() => SaveCount++;
        }
    }
}
