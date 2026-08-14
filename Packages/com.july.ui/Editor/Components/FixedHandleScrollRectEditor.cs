using UnityEditor;
using UnityEditor.UI;

namespace July.UI.Editor
{
    [CustomEditor(typeof(FixedHandleScrollRect), true)]
    public class FixedHandleScrollRectEditor : ScrollRectEditor
    {
        private SerializedProperty _fixedHandleSize;
        private SerializedProperty _handleSizeRatio;

        protected override void OnEnable()
        {
            base.OnEnable();
            _fixedHandleSize = serializedObject.FindProperty(nameof(_fixedHandleSize));
            _handleSizeRatio = serializedObject.FindProperty(nameof(_handleSizeRatio));
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fixed Handle", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_fixedHandleSize);
            if (_fixedHandleSize.boolValue)
                EditorGUILayout.PropertyField(_handleSizeRatio);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
