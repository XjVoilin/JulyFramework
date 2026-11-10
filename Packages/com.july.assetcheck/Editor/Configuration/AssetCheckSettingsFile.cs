using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace July.AssetCheck
{
    public sealed class AssetCheckSettingsFile
    {
        public const string FileName = "ProjectSettings/July/AssetCheck/settings.json";
        internal const string StateFileName = "Library/July/AssetCheck/state.json";
        [Serializable] sealed class ViewState { public string scope = "Assets"; }
        private readonly string _path;
        private string _loadedText;
        internal string ProjectDirectory { get; }

        public AssetCheckSettingsFile(string projectDirectory)
        {
            ProjectDirectory = Path.GetFullPath(projectDirectory);
            _path = Path.Combine(ProjectDirectory, FileName);
        }

        public AssetCheckSettings LoadOrCreate()
        {
            var settings = Read(out var needsSave);
            if (needsSave) Save(settings);
            return settings;
        }

        // 菜单状态校验只读取配置，不创建或升级磁盘文件。
        internal AssetCheckSettings ReadExistingOrDefault() => Read(out _);

        private AssetCheckSettings Read(out bool needsSave)
        {
            needsSave = false;
            if (!File.Exists(_path))
            {
                _loadedText = null;
                needsSave = true;
                var defaults = AssetCheckSettings.CreateDefault();
                defaults.scope = ReadScope();
                return defaults;
            }

            var text = File.ReadAllText(_path);
            AssetCheckSettings settings;
            try
            {
                settings = JsonUtility.FromJson<AssetCheckSettings>(text);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("资源检查配置无法解析，请修复 JSON 或 Git 冲突后重新加载。", exception);
            }
            if (settings == null)
                throw new InvalidDataException("资源检查配置不能为空。");
            _loadedText = text;
            settings.scope = ReadScope();
            settings.Validate();
            needsSave |= settings.CaptureIgnoredFolderPaths();
            return settings;
        }

        public void Save(AssetCheckSettings settings)
        {
            settings.Validate();
            EnsureUnchanged();
            settings.CaptureIgnoredFolderPaths();

            var text = JsonUtility.ToJson(settings, true) + Environment.NewLine;
            var stored = _loadedText == null ? null : JsonUtility.ToJson(JsonUtility.FromJson<AssetCheckSettings>(_loadedText), true) + Environment.NewLine;
            if (text != stored)
            {
                Write(_path, text);
                _loadedText = text;
            }
            string statePath = Path.Combine(ProjectDirectory, StateFileName);
            string state = JsonUtility.ToJson(new ViewState { scope = settings.scope });
            if (!File.Exists(statePath) || File.ReadAllText(statePath) != state) Write(statePath, state);
        }

        string ReadScope()
        {
            string path = Path.Combine(ProjectDirectory, StateFileName);
            if (!File.Exists(path)) return "Assets";
            var state = JsonUtility.FromJson<ViewState>(File.ReadAllText(path));
            if (state == null) throw new InvalidDataException("资源检查面板状态无效：" + path);
            AssetCheckSettings.ValidateScope(state.scope);
            return state.scope;
        }
        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal bool HasExternalChanges => (File.Exists(_path) ? File.ReadAllText(_path) : null) != _loadedText;

        internal void EnsureUnchanged()
        {
            if (HasExternalChanges)
                throw new IOException("配置文件已被外部修改或移除，请先重新加载配置，避免使用旧标准或覆盖其他人的改动。");
        }
    }
}
