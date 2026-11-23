using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    internal sealed class MeshRendererSortingOrderWindow : EditorWindow
    {
        [SerializeField] private MeshRenderer[] _renderers = Array.Empty<MeshRenderer>();
        [SerializeField] private int _order;

        [MenuItem("CONTEXT/MeshRenderer/修改 Order in Layer")]
        private static void Open(MenuCommand command)
        {
            var renderer = (MeshRenderer)command.context;
            var selection = Selection.gameObjects;
            var window = GetWindow<MeshRendererSortingOrderWindow>(true, "渲染顺序");
            window._renderers = selection.Contains(renderer.gameObject)
                ? selection.SelectMany(selected => selected.GetComponents<MeshRenderer>()).ToArray()
                : new[] { renderer };
            window._order = renderer.sortingOrder;
            window.minSize = new Vector2(290f, 76f);
            window.maxSize = new Vector2(520f, 76f);
            window.Repaint();
        }

        private void OnGUI()
        {
            if (_renderers.Length == 0 || _renderers.Any(renderer => renderer == null))
            {
                Close();
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
            {
                EditorGUILayout.LabelField(_renderers.Length == 1 ? _renderers[0].name : $"{_renderers.Length} 个 Mesh Renderer");
                _order = Mathf.Clamp(EditorGUILayout.IntField("Order in Layer", _order), short.MinValue, short.MaxValue);
                if (GUILayout.Button("应用"))
                {
                    ApplyOrder();
                    Close();
                }
            }
        }

        private void ApplyOrder()
        {
            using var renderers = new SerializedObject(_renderers);
            renderers.FindProperty("m_SortingOrder").intValue = _order;
            renderers.ApplyModifiedProperties();
        }
    }
}
