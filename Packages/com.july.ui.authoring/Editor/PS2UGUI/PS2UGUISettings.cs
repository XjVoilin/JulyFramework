using UnityEngine;
using UnityEditor;

namespace July.UI.Authoring.Editor
{
    [FilePath("ProjectSettings/July/UIAuthoring/settings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class PS2UGUISettings : ScriptableSingleton<PS2UGUISettings>
    {
        public string FontMaterialPath = "Assets/Game/Art/Fonts/";
        public string DefaultFontAssetPath = "Assets/Game/Art/Fonts/Font_Main.asset";
        public string DefaultBoxTargetPath = "Assets/Game/Art/Textures";
        public string DefaultMiniGameTargetPath = "Assets/Game/MiniGames/Game101/Art/Textures";

        public static PS2UGUISettings Instance => instance;
        public static string GetFontMaterialPath() => instance.FontMaterialPath;
        public static string GetDefaultFontAssetPath() => instance.DefaultFontAssetPath;
        public static string GetDefaultBoxTargetPath() => instance.DefaultBoxTargetPath;
        public static string GetDefaultMiniGameTargetPath() => instance.DefaultMiniGameTargetPath;

        [SettingsProvider]
        static SettingsProvider CreateProvider() => new SettingsProvider("Project/July/UI制作", SettingsScope.Project)
        {
            label = "UI制作",
            guiHandler = _ =>
            {
                var data = new SerializedObject(instance);
                data.Update();
                using (var change = new EditorGUI.ChangeCheckScope())
                {
                    EditorGUILayout.PropertyField(data.FindProperty(nameof(FontMaterialPath)), new GUIContent("字体材质目录"));
                    EditorGUILayout.PropertyField(data.FindProperty(nameof(DefaultFontAssetPath)), new GUIContent("默认字体"));
                    EditorGUILayout.PropertyField(data.FindProperty(nameof(DefaultBoxTargetPath)), new GUIContent("默认图片目录"));
                    EditorGUILayout.PropertyField(data.FindProperty(nameof(DefaultMiniGameTargetPath)), new GUIContent("默认小游戏目录"));
                    if (change.changed) { data.ApplyModifiedProperties(); instance.Save(true); }
                }
            }
        };
    }
}
