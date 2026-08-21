using UnityEditor;
using UnityEngine;

namespace July.UI.Editor
{
    [CustomEditor(typeof(UIModelPreview))]
    internal sealed class UIModelPreviewEditor : UnityEditor.Editor
    {
        private SerializedProperty _overallScale;
        private SerializedProperty _verticalAnchor;
        private SerializedProperty _verticalOffset;
        private SerializedProperty _horizontalSpacing;

        private void OnEnable()
        {
            _overallScale = serializedObject.FindProperty(nameof(_overallScale));
            _verticalAnchor = serializedObject.FindProperty(nameof(_verticalAnchor));
            _verticalOffset = serializedObject.FindProperty(nameof(_verticalOffset));
            _horizontalSpacing = serializedObject.FindProperty(nameof(_horizontalSpacing));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _overallScale,
                new GUIContent("Overall Scale"));
            EditorGUILayout.PropertyField(_verticalAnchor);
            EditorGUILayout.PropertyField(
                _verticalOffset,
                new GUIContent("Vertical Offset"));
            EditorGUILayout.PropertyField(_horizontalSpacing);

            serializedObject.ApplyModifiedProperties();

            if (EditorApplication.isPlaying)
                Repaint();
        }
    }
}
