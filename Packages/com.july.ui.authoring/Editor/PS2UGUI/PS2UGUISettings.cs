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
    }
}
