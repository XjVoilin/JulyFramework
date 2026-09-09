using UnityEditor;

namespace July.Release.Editor
{
    /// <summary>
    /// Editor 侧目标平台偏好持久化（EditorPrefs）。
    /// 由 <see cref="PlatformPanel"/> 写入，构建流水线 / BuildToolContext 读取。
    /// </summary>
    public static class EditorPlatformPref
    {
        private const string Key = "PlatformHelper_EditorPlatform";

        public static string Platform
        {
            get => ProjectEditorPrefs.GetString(Key, PlatformKeys.WeChat);
            set => ProjectEditorPrefs.SetString(Key, value);
        }
    }
}
