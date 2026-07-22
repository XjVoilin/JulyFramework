using UnityEngine;

namespace July.Persistence
{
    /// <summary>Player preferences routed to the active mini-game platform when required.</summary>
    public static class PlatformPreferences
    {
        public static string GetString(string key, string defaultValue = "")
        {
#if JULYGF_DY_MINIGAME
            return TTSDK.TT.PlayerPrefs.GetString(key, defaultValue);
#else
            return PlayerPrefs.GetString(key, defaultValue);
#endif
        }

        public static void SetString(string key, string value)
        {
#if JULYGF_DY_MINIGAME
            TTSDK.TT.PlayerPrefs.SetString(key, value);
#else
            PlayerPrefs.SetString(key, value);
#endif
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
#if JULYGF_DY_MINIGAME
            return TTSDK.TT.PlayerPrefs.GetInt(key, defaultValue);
#else
            return PlayerPrefs.GetInt(key, defaultValue);
#endif
        }

        public static void SetInt(string key, int value)
        {
#if JULYGF_DY_MINIGAME
            TTSDK.TT.PlayerPrefs.SetInt(key, value);
#else
            PlayerPrefs.SetInt(key, value);
#endif
        }

        public static float GetFloat(string key, float defaultValue = 0f)
        {
#if JULYGF_DY_MINIGAME
            return TTSDK.TT.PlayerPrefs.GetFloat(key, defaultValue);
#else
            return PlayerPrefs.GetFloat(key, defaultValue);
#endif
        }

        public static void SetFloat(string key, float value)
        {
#if JULYGF_DY_MINIGAME
            TTSDK.TT.PlayerPrefs.SetFloat(key, value);
#else
            PlayerPrefs.SetFloat(key, value);
#endif
        }

        public static bool HasKey(string key)
        {
#if JULYGF_DY_MINIGAME
            return TTSDK.TT.PlayerPrefs.HasKey(key);
#else
            return PlayerPrefs.HasKey(key);
#endif
        }

        public static void DeleteKey(string key)
        {
#if JULYGF_DY_MINIGAME
            TTSDK.TT.PlayerPrefs.DeleteKey(key);
#else
            PlayerPrefs.DeleteKey(key);
#endif
        }

        public static void DeleteAll()
        {
#if JULYGF_DY_MINIGAME
            TTSDK.TT.PlayerPrefs.DeleteAll();
#else
            PlayerPrefs.DeleteAll();
#endif
        }

        public static void Save()
        {
#if JULYGF_DY_MINIGAME
            TTSDK.TT.PlayerPrefs.Save();
#else
            PlayerPrefs.Save();
#endif
        }
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
