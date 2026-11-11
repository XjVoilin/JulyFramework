using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using July.Logging;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private string[] _scanFolders = Array.Empty<string>();
        private string _checkingPath;
        private bool _stepping, _invalidateRequested;
        internal bool Running => _task != null;
        internal bool Stale { get; private set; }
        internal float Progress { get; private set; }
        internal string Status { get; private set; } = "确认 JSON 来源或 Prefab／场景目录后开始检查。";
        internal string Failure { get; private set; }
        internal FontCheckResult Result { get; private set; }
        internal FontCheckSettings Settings => _workspace().Settings.fonts;
        internal TMP_FontAsset TargetFont => TMP_Settings.LoadDefaultSettings() == null ? null : TMP_Settings.defaultFontAsset;

        internal FontCheckSession(Func<AssetCheckWorkspace> workspace, Action changed)
        {
            _workspace = workspace; _changed = changed;
            AssetCheckAssetChanges.Changed += AssetsChanged;
            EditorSceneManager.sceneDirtied += SceneDirtied;
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

        internal void AddScanFolder(string path)
        {
            if (!(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(path))
                throw new ArgumentException("请选择 Assets 下的有效目录：" + path);
            var guid = AssetDatabase.AssetPathToGUID(path);
            Configure(settings => { if (!settings.scanFolderGuids.Contains(guid)) settings.scanFolderGuids.Add(guid); });
        }

        internal void RemoveScanFolder(string guid) => Configure(settings => settings.scanFolderGuids.Remove(guid));

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
            var sources = Settings.sourceGuids.Select(RequirePath).ToArray();
            if (sources.Length > 0 && font == null) throw new InvalidOperationException("JSON 检查需要 TMP 默认字体，请先设置默认字体。");
            var characterPath = sources.Length > 0 ? RequirePath(Settings.characterFileGuid) : null;
            _scanFolders = Settings.scanFolderGuids.Select(guid =>
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path) || !(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Prefab／场景目录已移除或不在 Assets 下：" + guid);
                return path;
            }).ToArray();
            var assets = _scanFolders.Length == 0 ? Array.Empty<string>() : AssetDatabase.FindAssets("t:Prefab", _scanFolders)
                .Concat(AssetDatabase.FindAssets("t:Scene", _scanFolders)).Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsHierarchyAsset).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (sources.Length == 0 && assets.Length == 0) throw new InvalidOperationException("请添加 JSON 来源，或包含 Prefab／场景的扫描目录。");
            _font = font;
            _settingsKey = JsonUtility.ToJson(Settings);
            _inputs = new HashSet<string>(sources.Concat(assets).Concat(_scanFolders).Concat(AssetDatabase.GetDependencies(assets, true)), StringComparer.OrdinalIgnoreCase);
            if (characterPath != null) _inputs.Add(characterPath);
            if (font != null) _inputs.Add(AssetDatabase.GetAssetPath(font));
            Result = null; Failure = null; Stale = false; Progress = 0;
            Status = "正在检查字符覆盖…";
            _task = Scan(sources, assets, characterPath, font).GetEnumerator();
            _changed();
        }

        private IEnumerable<bool> Scan(string[] sources, string[] assets, string characterPath, TMP_FontAsset font)
        {
            _checkingPath = characterPath;
            var available = characterPath == null ? new HashSet<uint>() : FontJsonCharacters.Read(characterPath);
            var required = new Dictionary<uint, HashSet<FontTextSource>>();
            var fonts = new Dictionary<TMP_FontAsset, FontCoverage>();
            var unverified = new List<FontTextSource>();
            var total = sources.Length + assets.Length;
            var textCount = 0;
            for (var index = 0; index < sources.Length; index++)
            {
                _checkingPath = sources[index];
                var source = new FontTextSource(sources[index]);
                var characters = FontJsonCharacters.Read(sources[index]);
                foreach (var character in characters)
                {
                    if (!required.TryGetValue(character, out var paths)) required.Add(character, paths = new HashSet<FontTextSource>());
                    paths.Add(source);
                }
                AddCoverage(fonts, font, source, characters);
                Progress = (float)(index + 1) / total;
                Status = $"已检查 {index + 1}/{total}：{sources[index]}";
                yield return true;
            }
            for (var index = 0; index < assets.Length; index++)
            {
                _checkingPath = assets[index];
                foreach (var text in FontAssetTextScan.Read(assets[index], font))
                {
                    if (text.Source.Location != null) textCount++;
                    if (!string.IsNullOrEmpty(text.Source.Limitation)) unverified.Add(text.Source);
                    else AddCoverage(fonts, text.Font, text.Source, text.Characters);
                    Status = $"正在检查 {sources.Length + index + 1}/{total}：{text.Source.Label}";
                    yield return true;
                }
                Progress = (float)(sources.Length + index + 1) / total;
                Status = $"已检查 {sources.Length + index + 1}/{total}：{assets[index]}";
                yield return true;
            }
            Result = new FontCheckResult(required, available, fonts.Values, unverified, sources.Length, assets.Length, textCount);
            Status = $"检查完成：{sources.Length} 个 JSON，{assets.Length} 个 Prefab／场景，{textCount} 个非空 TMP 文本；" +
                $"已比对 {Result.CharacterCount} 个去重字符，{unverified.Count} 个文本未验证。";
        }

        private void AddCoverage(Dictionary<TMP_FontAsset, FontCoverage> fonts, TMP_FontAsset font, FontTextSource source, HashSet<uint> characters)
        {
            if (!fonts.TryGetValue(font, out var coverage)) fonts.Add(font, coverage = new FontCoverage(font));
            _inputs.Add(coverage.FontPath);
            coverage.Add(characters, source);
        }

        internal void Pump()
        {
            SynchronizeSettings();
            if (!Running) return;
            try
            {
                _stepping = true;
                if (!_task.MoveNext()) Stop();
            }
            catch (Exception exception)
            {
                // JSON、资源读取及编辑器操作边界：失败显式显示，不能保留通过结果。
                Stop(); Result = null; Failure = _checkingPath + "\n" + exception.Message; Status = "检查失败，未生成完整结果。";
                JLogger.LogException(exception);
            }
            finally { _stepping = false; }
            if (_invalidateRequested) { _invalidateRequested = false; Invalidate(); }
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
                IsHierarchyAsset(path) && _scanFolders.Any(folder => path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) ||
                IsJson(path) && Path.GetFileNameWithoutExtension(path).IndexOf("language", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !Settings.excludedSourceGuids.Contains(AssetDatabase.AssetPathToGUID(path)))) Invalidate();
        }

        private void SceneDirtied(Scene scene)
        {
            if ((Result != null || Running) && _inputs.Contains(scene.path)) Invalidate();
        }

        private void Invalidate()
        {
            // 资源加载期间的编辑器回调不能重入并 Dispose 正在执行的枚举器。
            if (_stepping) { _invalidateRequested = true; return; }
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

        private static bool IsHierarchyAsset(string path) => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

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
            EditorSceneManager.sceneDirtied -= SceneDirtied;
        }
    }
}
