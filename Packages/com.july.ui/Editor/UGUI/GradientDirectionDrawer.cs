using UnityEditor;
using UnityEngine;

namespace July.UI.Editor
{
    /// <summary>
    /// Displays the eight UI/Gradient direction values without exceeding
    /// ShaderLab's argument limit for parameterized material drawers.
    /// </summary>
    public sealed class GradientDirectionDrawer : MaterialPropertyDrawer
    {
        private static readonly GUIContent[] DirectionNames =
        {
            new("Left To Right"),
            new("Right To Left"),
            new("Bottom To Top"),
            new("Top To Bottom"),
            new("Bottom Left To Top Right"),
            new("Top Right To Bottom Left"),
            new("Top Left To Bottom Right"),
            new("Bottom Right To Top Left"),
        };

        public override void OnGUI(
            Rect position,
            MaterialProperty property,
            GUIContent label,
            MaterialEditor editor)
        {
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();

            var currentDirection = Mathf.Clamp(Mathf.RoundToInt(property.floatValue), 0, DirectionNames.Length - 1);
            var selectedDirection = EditorGUI.Popup(position, label, currentDirection, DirectionNames);

            if (EditorGUI.EndChangeCheck())
                property.floatValue = selectedDirection;

            EditorGUI.showMixedValue = false;
        }
    }
}
