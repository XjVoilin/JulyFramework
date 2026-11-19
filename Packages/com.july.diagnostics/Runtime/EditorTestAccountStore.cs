#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Diagnostics
{
    internal static class EditorTestAccountStore
    {
        private static readonly string FilePath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "July", "Diagnostics", "test-accounts.json"));

        [Serializable]
        internal sealed class Entry
        {
            public string DeviceId;
            public string Note;
        }

        [Serializable]
        private sealed class State
        {
            public string Override = string.Empty;
            public List<Entry> Accounts = new();
        }

        private static State _current;

        private static State Current => _current ??= Load();

        internal static string Override => Current.Override;

        internal static IReadOnlyList<Entry> History => Current.Accounts;

        internal static void SwitchTo(string deviceId)
        {
            var accounts = new List<Entry>(Current.Accounts);
            var index = accounts.FindIndex(entry => string.Equals(entry.DeviceId, deviceId, StringComparison.Ordinal));
            var account = index >= 0 ? accounts[index] : new Entry { DeviceId = deviceId, Note = string.Empty };
            if (index >= 0)
                accounts.RemoveAt(index);

            accounts.Insert(0, account);
            Save(deviceId, accounts);
        }

        internal static void ClearOverride() => Save(string.Empty, Current.Accounts);

        internal static void SetNote(int index, string note)
        {
            var accounts = new List<Entry>(Current.Accounts);
            accounts[index] = new Entry { DeviceId = accounts[index].DeviceId, Note = note };
            Save(Current.Override, accounts);
        }

        internal static void RemoveHistoryEntry(int index)
        {
            var accounts = new List<Entry>(Current.Accounts);
            accounts.RemoveAt(index);
            Save(Current.Override, accounts);
        }

        private static State Load()
        {
            try
            {
                return Parse(File.ReadAllText(FilePath), FilePath);
            }
            catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return MigrateEditorPrefs();
            }
        }

        private static State MigrateEditorPrefs()
        {
            var prefix = "July.Diagnostics.TestAccount:" + Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            var overrideKey = prefix + ":Override";
            var historyKey = prefix + ":History";
            var overrideId = EditorPrefs.GetString(overrideKey, string.Empty);
            var history = EditorPrefs.GetString(historyKey, string.Empty);
            if (string.IsNullOrEmpty(overrideId) && string.IsNullOrEmpty(history))
                return new State();

            var state = string.IsNullOrEmpty(history) ? new State() : Parse(history, "旧版 EditorPrefs");
            state.Override = overrideId;
            Write(state);
            EditorPrefs.DeleteKey(overrideKey);
            EditorPrefs.DeleteKey(historyKey);
            return state;
        }

        private static State Parse(string json, string source)
        {
            State state;
            try
            {
                state = JsonUtility.FromJson<State>(json);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"无法读取测试账号状态：{source}", exception);
            }

            if (state?.Accounts == null ||
                state.Accounts.Exists(entry => entry == null || string.IsNullOrWhiteSpace(entry.DeviceId)))
            {
                throw new InvalidDataException($"测试账号状态无效：{source}");
            }

            return state;
        }

        private static void Save(string overrideId, List<Entry> accounts)
        {
            var next = new State { Override = overrideId, Accounts = accounts };
            Write(next);
            _current = next;
        }

        private static void Write(State state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var temporary = FilePath + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(state, true));
                if (File.Exists(FilePath))
                    File.Replace(temporary, FilePath, null);
                else
                    File.Move(temporary, FilePath);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
#endif
