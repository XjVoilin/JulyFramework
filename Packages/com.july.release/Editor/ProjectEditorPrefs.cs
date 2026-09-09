using System.IO;
using UnityEngine;
using UnityEditor;
namespace July.Release.Editor
{
    internal static class ProjectEditorPrefs
    {
        static string Key(string key) => "July.Release:" + Path.GetFullPath(Application.dataPath).Replace('\\', '/') + ":" + key;
        public static bool GetBool(string key, bool fallback) => EditorPrefs.GetBool(Key(key), fallback);
        public static int GetInt(string key, int fallback) => EditorPrefs.GetInt(Key(key), fallback);
        public static string GetString(string key, string fallback) => EditorPrefs.GetString(Key(key), fallback);
        public static void SetBool(string key, bool value) => EditorPrefs.SetBool(Key(key), value);
        public static void SetInt(string key, int value) => EditorPrefs.SetInt(Key(key), value);
        public static void SetString(string key, string value) => EditorPrefs.SetString(Key(key), value);
    }
}

