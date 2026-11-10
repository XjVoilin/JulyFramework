using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using July.Logging;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class FontCheckSession : IDisposable
    {
        private readonly Func<AssetCheckWorkspace> _workspace;
        private readonly Action _changed;
        private IEnumerator<bool> _task;
        private HashSet<string> _inputs = new(StringComparer.OrdinalIgnoreCase);
        private string _settingsKey;
        private TMP_FontAsset _font;
        internal bool Running => _task != null;
        internal bool Stale { get; private set; }
        internal float Progress { get; private set; }
        internal string Status { get; private set; } = "确认文案来源和字符文件后开始检查。";
        internal string Failure { get; private set; }
        internal FontCheckResult Result { get; private set; }
        internal FontCheckSettings Settings => _workspace().Settings.fonts;
        internal TMP_FontAsset TargetFont => TMP_Settings.LoadDefaultSettings() == null ? null : TMP_Settings.defaultFontAsset;

        internal FontCheckSession(Func<AssetCheckWorkspace> workspace, Action changed)
        {
            _workspace = workspace; _changed = changed;
            AssetCheckAssetChanges.Changed += AssetsChanged;
        }

        internal void RefreshSources()
        {
            RequireIdle();
            var discovered = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets" })
                .Select(guid => (Guid: guid, Path: AssetDatabase.GUIDToAssetPath(guid)))
                .Where(entry => IsJson(entry.Path) && Path.GetFileNameWithoutExtension(entry.Path)
                    .IndexOf("language", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
            Configure(settings =>
            {
                foreach (var entry in discovered)
                    if (!settings.sourceGuids.Contains(entry.Guid) && !settings.excludedSourceGuids.Contains(entry.Guid))
                        settings.sourceGuids.Add(entry.Guid);
                if (!settings.initialized)
                {
                    var candidates = discovered.Where(entry => string.Equals(Path.GetFileName(entry.Path),
                        "tblanguage.json", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (candidates.Length == 1) settings.characterFileGuid = candidates[0].Guid;
                    settings.initialized = true;
                }
            });
        }

        internal void AddSource(string path)
        {
            var guid = RequireJson(path);
            Configure(settings =>
            {
                settings.excludedSourceGuids.RemoveAll(value => value == guid);
                if (!settings.sourceGuids.Contains(guid)) settings.sourceGuids.Add(guid);
            });
        }

        internal void RemoveSource(string guid) => Configure(settings =>
        {
            if (settings.sourceGuids.Remove(guid) && !settings.excludedSourceGuids.Contains(guid))
                settings.excludedSourceGuids.Add(guid);
        });

        internal void SetCharacterFile(string path)
        {
            var guid = string.IsNullOrEmpty(path) ? "" : RequireJson(path);
            Configure(settings => settings.characterFileGuid = guid);
        }

        private void Configure(Action<FontCheckSettings> change)
        {
            RequireIdle();
            _workspace().ChangeSettings(settings => change(settings.fonts));
            SynchronizeSettings();
            _changed();
        }

        internal void Start()
        {
            RequireIdle();
            RefreshSources();
            if (_workspace().ConfigurationChanged) throw new InvalidOperationException("配置已被外部修改，请先重新加载配置。");
            var font = TargetFont;
            if (font == null) throw new InvalidOperationException("TMP Settings 未设置默认字体，请先设置默认字体。");
            if (Settings.sourceGuids.Count == 0) throw new InvalidOperationException("请至少添加一个文案来源 JSON。");
            var sources = Settings.sourceGuids.Select(RequirePath).ToArray();
            var characterPath = RequirePath(Settings.characterFileGuid);
            _font = font;
            _settingsKey = JsonUtility.ToJson(Settings);
            _inputs = new HashSet<string>(sources.Append(characterPath).Append(AssetDatabase.GetAssetPath(font)), StringComparer.OrdinalIgnoreCase);
            Result = null; Failure = null; Stale = false; Progress = 0;
            Status = "正在检查字符覆盖…";
            _task = Scan(sources, characterPath, font).GetEnumerator();
            _changed();
        }

        private IEnumerable<bool> Scan(string[] sources, string characterPath, TMP_FontAsset font)
        {
            var available = FontJsonCharacters.Read(characterPath);
            var fontCharacters = new HashSet<uint>(font.characterTable.Select(character => character.unicode));
            var required = new Dictionary<uint, HashSet<string>>();
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index];
                foreach (var character in FontJsonCharacters.Read(source))
                {
                    if (!required.TryGetValue(character, out var paths)) required.Add(character, paths = new HashSet<string>());
                    paths.Add(source);
                }
                Progress = (float)(index + 1) / sources.Length;
                Status = $"已检查 {index + 1}/{sources.Length}：{source}";
                yield return true;
            }
            Result = new FontCheckResult(required, available, fontCharacters, sources.Length);
            Status = $"检查完成：{sources.Length} 个来源，{required.Count} 个去重字符。";
        }

        internal void Pump()
        {
            SynchronizeSettings();
            if (!Running) return;
            try
            {
                if (!_task.MoveNext()) Stop();
            }
            catch (Exception exception)
            {
                // JSON、资源读取及编辑器操作边界：失败显式显示，不能保留通过结果。
                Stop(); Result = null; Failure = exception.Message; Status = "检查失败，未生成完整结果。";
                JLogger.LogException(exception);
            }
            _changed();
        }

        internal void SynchronizeSettings()
        {
            if (Result == null && !Running) return;
            if (_workspace().ConfigurationChanged || _settingsKey != JsonUtility.ToJson(Settings) || _font != TargetFont)
                Invalidate();
        }

        private void AssetsChanged(string[] paths)
        {
            if ((Result != null || Running) && paths.Any(path => _inputs.Contains(path) ||
                IsJson(path) && Path.GetFileNameWithoutExtension(path).IndexOf("language", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !Settings.excludedSourceGuids.Contains(AssetDatabase.AssetPathToGUID(path)))) Invalidate();
        }

        private void Invalidate()
        {
            if (Stale) return;
            Stop(); Stale = true; Status = "输入或配置已改变，结果已过期，请重新检查。"; _changed();
        }

        internal void Cancel()
        {
            if (!Running) return;
            Stop(); Result = null; Status = "已取消，未生成完整结果。"; _changed();
        }

        internal void CopyMissing()
        {
            SynchronizeSettings();
            if (Result == null || Running || Stale) throw new InvalidOperationException("请先完成当前输入的字体检查。");
            EditorGUIUtility.systemCopyBuffer = FontCheckResult.CopyText(Result.MissingCharacters);
        }

        private void Stop() { _task?.Dispose(); _task = null; }
        private void RequireIdle()
        {
            if (Running || _workspace().Running) throw new InvalidOperationException("请等待当前操作完成，或先取消。");
        }

        private static bool IsJson(string path) => path.StartsWith("Assets/", StringComparison.Ordinal) &&
            string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

        private static string RequireJson(string path)
        {
            if (!IsJson(path) || AssetDatabase.LoadAssetAtPath<TextAsset>(path) == null)
                throw new ArgumentException("请选择 Assets 下的 JSON 文本资源：" + path);
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static string RequirePath(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("JSON 资源未指定或已移除：" + guid);
            RequireJson(path);
            return path;
        }

        public void Dispose()
        {
            Stop(); AssetCheckAssetChanges.Changed -= AssetsChanged;
        }
    }
}
