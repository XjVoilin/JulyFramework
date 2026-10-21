using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace July.AssetCheck
{
    public sealed class AssetCheckSettingsFile
    {
        public const string FileName = "AssetCheckSettings.json";
        private readonly string _path;
        private string _loadedText;
        internal string ProjectDirectory => Path.GetDirectoryName(_path);

        public AssetCheckSettingsFile(string projectDirectory)
        {
            _path = Path.Combine(projectDirectory, FileName);
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
                return AssetCheckSettings.CreateDefault();
            }

            var text = File.ReadAllText(_path);
            AssetCheckSettings settings;
            try
            {
                settings = JsonUtility.FromJson<AssetCheckSettings>(text);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("AssetCheckSettings.json 无法解析，请修复 JSON 或 Git 冲突后重新加载。", exception);
            }
            if (settings == null)
                throw new InvalidDataException("AssetCheckSettings.json 不能为空。");
            _loadedText = text;
            if (settings.version == 1)
            {
                settings = settings.UpgradeVersionOne();
                needsSave = true;
            }
            else if (settings.version == 2)
            {
                settings = settings.UpgradeVersionTwo();
                needsSave = true;
            }
            else if (settings.version == 3)
            {
                settings = settings.UpgradeVersionThree();
                needsSave = true;
            }
            else
                settings.Validate();
            return settings;
        }

        public void Save(AssetCheckSettings settings)
        {
            settings.Validate();
            EnsureUnchanged();

            var text = JsonUtility.ToJson(settings, true) + Environment.NewLine;
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, null);
            else
                File.Move(temporaryPath, _path);
            _loadedText = text;
        }

        internal bool HasExternalChanges => (File.Exists(_path) ? File.ReadAllText(_path) : null) != _loadedText;

        internal void EnsureUnchanged()
        {
            if (HasExternalChanges)
                throw new IOException("配置文件已被外部修改或移除，请先重新加载配置，避免使用旧标准或覆盖其他人的改动。");
        }
    }
}
