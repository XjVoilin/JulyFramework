using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    // Keep the shader's existing sRGB vector storage and explicit color conversion.
    public sealed class MeshHighlightColorDrawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty property, string label, MaterialEditor editor)
        {
            var value = property.vectorValue;
            var mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            var color = EditorGUI.ColorField(position, new GUIContent(label),
                new Color(value.x, value.y, value.z, value.w), true, true, false);
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label);
                property.vectorValue = new Vector4(color.r, color.g, color.b, color.a);
            }
            EditorGUI.showMixedValue = mixed;
        }
    }
}
