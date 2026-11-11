using UnityEditor;
using UnityEngine;

namespace July.Config.Authoring
{
    internal static class ConfigAuthoringStyles
    {
        private static GUIStyle _page, _card, _title, _action;
        internal static GUIStyle Page => _page ?? (_page = new GUIStyle
        {
            padding = new RectOffset(16, 16, 14, 14)
        });
        internal static GUIStyle Card => _card ?? (_card = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(12, 12, 10, 12),
            margin = new RectOffset(0, 0, 0, 0)
        });
        internal static GUIStyle Title => _title ?? (_title = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            fixedHeight = 24
        });
        internal static GUIStyle Action => _action ?? (_action = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold
        });

        internal static void ReadOnlyField(string label, string value)
        {
            var row = EditorGUILayout.GetControlRect();
            var field = EditorGUI.PrefixLabel(row, new GUIContent(label));
            EditorGUI.SelectableLabel(field, value, EditorStyles.textField);
        }
    }
}
