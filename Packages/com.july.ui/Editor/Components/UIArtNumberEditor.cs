using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Editor
{
    [CustomEditor(typeof(UIArtNumber)), CanEditMultipleObjects]
    internal sealed class UIArtNumberEditor : UnityEditor.Editor
    {
        private int _previewNumber;

        public override void OnInspectorGUI()
        {
            var changed = DrawDefaultInspector();
            if (!Application.isPlaying)
            {
                EditorGUI.BeginChangeCheck();
                _previewNumber = Mathf.Max(0, EditorGUILayout.IntField("Preview Number", _previewNumber));
                changed |= EditorGUI.EndChangeCheck();
            }

            var layout = serializedObject.FindProperty("_layout");
            if (!layout.hasMultipleDifferentValues && layout.enumValueIndex == (int)UIArtNumberLayout.Manual)
                EditorGUILayout.HelpBox("Manual preserves slot positions. Choose Left, Center or Right to arrange digits using Spacing.", MessageType.Info);

            foreach (var item in targets)
            {
                var number = (UIArtNumber)item;
                var error = ConfigurationError(number);
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Warning);
                    continue;
                }
                if (changed)
                    ApplyPreview(number, Application.isPlaying ? number.CurrentNumber : _previewNumber);
            }
        }

        private static string ConfigurationError(UIArtNumber number)
        {
            var source = new SerializedObject(number);
            var slots = source.FindProperty("_digitSlots");
            if (slots.arraySize == 0) return "Assign digit slots to preview the number.";
            for (var i = 0; i < slots.arraySize; i++)
                if (!slots.GetArrayElementAtIndex(i).objectReferenceValue)
                    return $"Digit Slots [{i}] is missing an Image.";

            var digits = source.FindProperty("_digits");
            if (digits.arraySize != 10) return "Assign exactly ten digit sprites, ordered 0-9.";
            for (var i = 0; i < 10; i++)
                if (!digits.GetArrayElementAtIndex(i).objectReferenceValue)
                    return $"Digits [{i}] is missing a Sprite.";
            return null;
        }

        internal static void ApplyPreview(UIArtNumber number, int value)
        {
            var source = new SerializedObject(number);
            var slots = source.FindProperty("_digitSlots");
            var affected = new HashSet<Object>();
            for (var i = 0; i < slots.arraySize; i++)
            {
                var image = (Image)slots.GetArrayElementAtIndex(i).objectReferenceValue;
                affected.Add(image);
                affected.Add(image.rectTransform);
                affected.Add(image.gameObject);
            }
            var objects = new Object[affected.Count];
            affected.CopyTo(objects);
            Undo.RecordObjects(objects, "Preview Art Number");
            number.RefreshEditorPreview(value);
            foreach (var item in objects)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                EditorUtility.SetDirty(item);
            }
            SceneView.RepaintAll();
        }
    }
}
