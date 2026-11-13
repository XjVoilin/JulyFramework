using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace July.UI.Editor
{
    [CustomEditor(typeof(UIDisabledVisual))]
    internal sealed class UIDisabledVisualEditor : UnityEditor.Editor
    {
        private static readonly string[] GroupNames = { "Sprites", "Visibility", "Colors", "Text Materials" };
        private static readonly string[] PreviewNames = { "Off", "Normal", "Disabled" };
        private static readonly GUIContent PreviewLabel = new("Preview", "Temporary preview. Values are restored when you stop, save, or leave the Inspector.");

        private readonly List<ReorderableList> _lists = new();
        private readonly bool[] _expanded = new bool[4];
        private int _preview;

        private void OnEnable()
        {
            _lists.Clear();
            for (var i = 0; i < GroupNames.Length; i++)
            {
                var array = serializedObject.FindProperty(UIDisabledVisualAuthoring.BindingArrays[i]);
                _expanded[i] = array.arraySize > 0;
                var list = new ReorderableList(serializedObject, array, true, false, true, true)
                {
                    headerHeight = 0,
                    elementHeight = (EditorGUIUtility.singleLineHeight + 2) * 2 + 6
                };
                list.drawElementCallback = (rect, index, _, _) => DrawBinding(rect, array.GetArrayElementAtIndex(index));
                list.onAddCallback = _ =>
                {
                    var index = array.arraySize++;
                    var item = array.GetArrayElementAtIndex(index);
                    item.FindPropertyRelative("_target").objectReferenceValue = null;
                    ResetValue(item.FindPropertyRelative("_normal"), true);
                    ResetValue(item.FindPropertyRelative("_disabled"), false);
                    list.index = index;
                };
                _lists.Add(list);
            }
        }

        private static void DrawBinding(Rect rect, SerializedProperty item)
        {
            rect.y += 3;
            rect.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(rect, item.FindPropertyRelative("_target"), GUIContent.none);
            rect.y += EditorGUIUtility.singleLineHeight + 2;
            rect.width = (rect.width - 8) / 2;
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 60;
            EditorGUI.PropertyField(rect, item.FindPropertyRelative("_normal"), new GUIContent("Normal"));
            rect.x += rect.width + 8;
            EditorGUI.PropertyField(rect, item.FindPropertyRelative("_disabled"), new GUIContent("Disabled"));
            EditorGUIUtility.labelWidth = labelWidth;
        }

        private static void ResetValue(SerializedProperty property, bool normal)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference: property.objectReferenceValue = null; break;
                case SerializedPropertyType.Boolean: property.boolValue = normal; break;
                case SerializedPropertyType.Color: property.colorValue = normal ? Color.white : Color.gray; break;
            }
        }

        private void OnDisable()
        {
            if (UIDisabledVisualPreview.Owner == target) UIDisabledVisualPreview.Stop();
        }

        public override void OnInspectorGUI()
        {
            var visual = (UIDisabledVisual)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(UIDisabledVisualPreview.Owner || Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_disabled"), new GUIContent("Initially Disabled", "Used for standalone visuals. A bound button supplies the state."));
                for (var i = 0; i < _lists.Count; i++)
                {
                    var list = _lists[i];
                    _expanded[i] = EditorGUILayout.Foldout(_expanded[i], $"{GroupNames[i]} ({list.count})", true);
                    if (_expanded[i]) list.DoLayoutList();
                }
                serializedObject.ApplyModifiedProperties();
            }

            var errors = UIDisabledVisualAuthoring.ConfigurationErrors(visual);
            DrawToolbar(visual, errors.Count > 0);
            if (errors.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
        }

        private void DrawToolbar(UIDisabledVisual visual, bool hasErrors)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(UIDisabledVisualPreview.Owner || Application.isPlaying))
                {
                    if (GUILayout.Button(new GUIContent("Capture Normal", "Capture the current values of configured targets as the Normal state. Supports Undo."), EditorStyles.miniButton, GUILayout.Width(106)))
                    {
                        UIDisabledVisualAuthoring.CaptureNormal(visual);
                        serializedObject.Update();
                    }
                }

                GUILayout.FlexibleSpace();
                var previewing = UIDisabledVisualPreview.Owner == visual;
                if (!previewing) _preview = 0;
                using (new EditorGUI.DisabledScope(Application.isPlaying || (!previewing &&
                    (hasErrors || AnimationMode.InAnimationMode()))))
                {
                    var labelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 48;
                    var next = EditorGUILayout.Popup(PreviewLabel, _preview, PreviewNames, GUILayout.Width(160));
                    EditorGUIUtility.labelWidth = labelWidth;
                    if (next != _preview)
                    {
                        if (next == 0) UIDisabledVisualPreview.Stop();
                        else UIDisabledVisualPreview.Show(visual, next == 2);
                        _preview = next;
                    }
                }
            }
        }
    }
}
