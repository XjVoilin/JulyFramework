using System;
using System.Collections.Generic;
using System.Linq;
using July.RedDot;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace July.RedDot.Editor
{
    /// <summary>UIRedDot inspector backed by the project's RedDotTreeConfig assets.</summary>
    [CustomEditor(typeof(UIRedDot))]
    internal sealed class UIRedDotEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProperty;
        private List<RedDotKeyOption> _options;
        private Dictionary<string, RedDotKeyOption> _optionsByRuntimeKey;
        private AdvancedDropdownState _dropdownState;

        private void OnEnable()
        {
            _keyProperty = serializedObject.FindProperty("_key");
            _dropdownState = new AdvancedDropdownState();
            BuildOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            DrawKeySelector();

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

        private void DrawKeySelector()
        {
            if (_options == null)
                BuildOptions();

            var key = _keyProperty.stringValue;
            _optionsByRuntimeKey.TryGetValue(key ?? string.Empty, out var selectedOption);
            if (!string.IsNullOrEmpty(key) && selectedOption == null)
            {
                EditorGUILayout.HelpBox(
                    $"Runtime key '{key}' is absent from all RedDotTreeConfig assets.",
                    MessageType.Warning);
            }

            var row = EditorGUILayout.GetControlRect();
            var buttonRect = EditorGUI.PrefixLabel(row, new GUIContent("Red Dot Key"));
            var buttonLabel = selectedOption?.CompactLabel
                              ?? (string.IsNullOrEmpty(key) ? "(None)" : key);
            var tooltip = selectedOption?.DisplayPath ?? key;

            if (!EditorGUI.DropdownButton(
                    buttonRect,
                    new GUIContent(buttonLabel, tooltip),
                    FocusType.Keyboard))
                return;

            new RedDotKeyDropdown(
                _dropdownState,
                _options,
                SetSelectedKey)
                .Show(buttonRect);
        }

        private void SetSelectedKey(string runtimeKey)
        {
            serializedObject.Update();
            _keyProperty.stringValue = runtimeKey ?? string.Empty;
            serializedObject.ApplyModifiedProperties();
        }

        private void BuildOptions()
        {
            var optionsByRuntimeKey = new Dictionary<string, RedDotKeyOption>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:RedDotTreeConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<RedDotTreeConfig>(path);
                if (config?.nodes == null) continue;
                foreach (var node in config.nodes)
                {
                    if (string.IsNullOrWhiteSpace(node?.key)) continue;

                    var runtimeKey = config.GetRuntimeKey(node);
                    if (string.IsNullOrEmpty(runtimeKey)) continue;

                    var displayPath = config.GetDisplayPath(node);
                    var searchLabel = string.IsNullOrWhiteSpace(node.description)
                        ? displayPath
                        : $"{displayPath}  —  {node.description}";

                    if (!optionsByRuntimeKey.ContainsKey(runtimeKey))
                    {
                        optionsByRuntimeKey.Add(
                            runtimeKey,
                            new RedDotKeyOption(runtimeKey, displayPath, searchLabel));
                    }
                }
            }

            _options = optionsByRuntimeKey.Values
                .OrderBy(option => option.DisplayPath, StringComparer.Ordinal)
                .ToList();
            _optionsByRuntimeKey = optionsByRuntimeKey;
        }

        private sealed class RedDotKeyDropdown : AdvancedDropdown
        {
            private readonly IReadOnlyList<RedDotKeyOption> _options;
            private readonly Action<string> _onSelected;

            public RedDotKeyDropdown(
                AdvancedDropdownState state,
                IReadOnlyList<RedDotKeyOption> options,
                Action<string> onSelected)
                : base(state)
            {
                _options = options;
                _onSelected = onSelected;
                minimumSize = new Vector2(420f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Red Dot Keys");
                root.AddChild(new RedDotKeyDropdownItem("(None)", string.Empty));

                foreach (var option in _options)
                    root.AddChild(new RedDotKeyDropdownItem(option.SearchLabel, option.RuntimeKey));

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is RedDotKeyDropdownItem keyItem)
                    _onSelected(keyItem.RuntimeKey);
            }
        }

        private sealed class RedDotKeyDropdownItem : AdvancedDropdownItem
        {
            public RedDotKeyDropdownItem(string name, string runtimeKey) : base(name)
            {
                RuntimeKey = runtimeKey;
            }

            public string RuntimeKey { get; }
        }

        private sealed class RedDotKeyOption
        {
            public RedDotKeyOption(string runtimeKey, string displayPath, string searchLabel)
            {
                RuntimeKey = runtimeKey;
                DisplayPath = displayPath;
                SearchLabel = searchLabel;
                CompactLabel = BuildCompactLabel(runtimeKey);
            }

            public string RuntimeKey { get; }
            public string DisplayPath { get; }
            public string SearchLabel { get; }
            public string CompactLabel { get; }

            private static string BuildCompactLabel(string runtimeKey)
            {
                var segments = runtimeKey.Split('/');
                return segments.Length switch
                {
                    0 => "(None)",
                    1 => segments[0],
                    2 => $"{segments[0]} / {segments[1]}",
                    _ => $"… / {segments[^2]} / {segments[^1]}"
                };
            }
        }
    }
}
