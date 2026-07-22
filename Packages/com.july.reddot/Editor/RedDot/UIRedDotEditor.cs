using System;
using System.Collections.Generic;
using July.RedDot;
using UnityEditor;

namespace July.RedDot.Editor
{
    /// <summary>UIRedDot inspector backed by the project's RedDotTreeConfig assets.</summary>
    [CustomEditor(typeof(UIRedDot))]
    internal sealed class UIRedDotEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProperty;
        private string[] _popupLabels;
        private string[] _popupValues;

        private void OnEnable()
        {
            _keyProperty = serializedObject.FindProperty("_key");
            BuildOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            DrawKeyPopup();

            var property = serializedObject.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name is "m_Script" or "_key") continue;
                EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawKeyPopup()
        {
            if (_popupValues == null) BuildOptions();

            var key = _keyProperty.stringValue;
            var index = Array.IndexOf(_popupValues, key);
            if (!string.IsNullOrEmpty(key) && index < 0)
            {
                EditorGUILayout.HelpBox(
                    $"Key '{key}' is absent from all RedDotTreeConfig assets.",
                    MessageType.Warning);
            }

            var selected = string.IsNullOrEmpty(key) ? 0 : index >= 0 ? index + 1 : 0;
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup("Red Dot Key", selected, _popupLabels);
            if (EditorGUI.EndChangeCheck())
                _keyProperty.stringValue = next == 0 ? string.Empty : _popupValues[next - 1];
        }

        private void BuildOptions()
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:RedDotTreeConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<RedDotTreeConfig>(path);
                if (config?.nodes == null) continue;
                foreach (var node in config.nodes)
                {
                    if (!string.IsNullOrWhiteSpace(node?.key))
                        keys.Add(node.key);
                }
            }

            _popupValues = new string[keys.Count];
            keys.CopyTo(_popupValues);
            _popupLabels = new string[_popupValues.Length + 1];
            _popupLabels[0] = "(None)";
            for (var i = 0; i < _popupValues.Length; i++)
                _popupLabels[i + 1] = _popupValues[i];
        }
    }
}
