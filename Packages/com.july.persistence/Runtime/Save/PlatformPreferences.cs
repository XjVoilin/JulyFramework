using System;
using UnityEngine;

namespace July.Persistence
{
    /// <summary>Storage surface used by platform-aware player preferences.</summary>
    public interface IPlatformPreferencesAdapter
    {
        string GetString(string key, string defaultValue = "");
        void SetString(string key, string value);
        int GetInt(string key, int defaultValue = 0);
        void SetInt(string key, int value);
        float GetFloat(string key, float defaultValue = 0f);
        void SetFloat(string key, float value);
        bool HasKey(string key);
        void DeleteKey(string key);
        void DeleteAll();
        void Save();
    }

    /// <summary>Default Unity PlayerPrefs adapter.</summary>
    public sealed class UnityPlayerPreferencesAdapter : IPlatformPreferencesAdapter
    {
        public string GetString(string key, string defaultValue = "") =>
            PlayerPrefs.GetString(key, defaultValue);

        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public int GetInt(string key, int defaultValue = 0) =>
            PlayerPrefs.GetInt(key, defaultValue);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public float GetFloat(string key, float defaultValue = 0f) =>
            PlayerPrefs.GetFloat(key, defaultValue);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public void DeleteAll() => PlayerPrefs.DeleteAll();
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>
    /// Player preferences routed through an explicitly selected platform adapter.
    /// Unity PlayerPrefs is used until the application composition root selects another adapter.
    /// </summary>
    public static class PlatformPreferences
    {
        private static IPlatformPreferencesAdapter _adapter =
            new UnityPlayerPreferencesAdapter();

        public static void SetAdapter(IPlatformPreferencesAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public static void UseUnityPlayerPrefs() =>
            _adapter = new UnityPlayerPreferencesAdapter();

        public static string GetString(string key, string defaultValue = "") =>
            _adapter.GetString(key, defaultValue);

        public static void SetString(string key, string value) =>
            _adapter.SetString(key, value);

        public static int GetInt(string key, int defaultValue = 0) =>
            _adapter.GetInt(key, defaultValue);

        public static void SetInt(string key, int value) => _adapter.SetInt(key, value);

        public static float GetFloat(string key, float defaultValue = 0f) =>
            _adapter.GetFloat(key, defaultValue);

        public static void SetFloat(string key, float value) =>
            _adapter.SetFloat(key, value);

        public static bool HasKey(string key) => _adapter.HasKey(key);
        public static void DeleteKey(string key) => _adapter.DeleteKey(key);
        public static void DeleteAll() => _adapter.DeleteAll();
        public static void Save() => _adapter.Save();
    }

    public sealed class PlatformPreferencesKeyValueStore : IKeyValueStore
    {
        public bool HasKey(string key) => PlatformPreferences.HasKey(key);
        public string GetString(string key) => PlatformPreferences.GetString(key);
        public void SetString(string key, string value) =>
            PlatformPreferences.SetString(key, value);
        public void DeleteKey(string key) => PlatformPreferences.DeleteKey(key);
        public void Save() => PlatformPreferences.Save();
    }

    public sealed class PlatformPreferencesSaveSystem : KeyValueSaveSystem
    {
        public PlatformPreferencesSaveSystem(string keyPrefix = "Save_")
            : base(new PlatformPreferencesKeyValueStore(), keyPrefix)
        {
        }
    }
}
