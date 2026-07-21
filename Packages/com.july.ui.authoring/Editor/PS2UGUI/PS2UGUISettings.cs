using UnityEngine;
using UnityEditor;

namespace July.UI.Authoring.Editor
{
    [CreateAssetMenu(fileName = "PS2UGUISettings", menuName = "JulyGF/PS2UGUI Settings")]
    public class PS2UGUISettings : ScriptableObject
    {
        [Header("Font")]
        [Tooltip("TMP 字体材质搜索目录")]
        public string FontMaterialPath = "Assets/Game/Art/Fonts/";
        
        [Tooltip("默认 TMP FontAsset 路径")]
        public string DefaultFontAssetPath = "Assets/Game/Art/Fonts/Font_Main.asset";

        [Header("Art Importer")]
        [Tooltip("盒子默认目标路径")]
        public string DefaultBoxTargetPath = "Assets/Game/Art/Textures";
        
        [Tooltip("小游戏默认目标路径模板")]
        public string DefaultMiniGameTargetPath = "Assets/Game/MiniGames/Game101/Art/Textures";

        private static PS2UGUISettings _instance;
        
        public static PS2UGUISettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    var guids = AssetDatabase.FindAssets("t:PS2UGUISettings");
                    if (guids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        _instance = AssetDatabase.LoadAssetAtPath<PS2UGUISettings>(path);
                    }
                }
                return _instance;
            }
        }

        public static string GetFontMaterialPath() => Instance?.FontMaterialPath ?? "Assets/Game/Art/Fonts/";
        public static string GetDefaultFontAssetPath() => Instance?.DefaultFontAssetPath ?? "Assets/Game/Art/Fonts/Font_Main.asset";
        public static string GetDefaultBoxTargetPath() => Instance?.DefaultBoxTargetPath ?? "Assets/Game/Art/Textures";
        public static string GetDefaultMiniGameTargetPath() => Instance?.DefaultMiniGameTargetPath ?? "Assets/Game/MiniGames/Game101/Art/Textures";
    }
}
