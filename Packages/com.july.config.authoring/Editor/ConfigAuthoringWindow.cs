using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Config.Authoring
{
    public class ConfigAuthoringWindow : EditorWindow
    {
        private IReadOnlyList<ConfigGenerationTask> _tasks;
        private readonly HashSet<string> _selected = new HashSet<string>();
        private Vector2 _scroll;
        protected IReadOnlyList<ConfigGenerationTask> Tasks => _tasks ?? (_tasks = ConfigGeneration.GetTasks());

        [MenuItem("JulyGF/配置表/生成窗口", priority = 10)]
        public static void ShowWindow() => OpenWindow<ConfigAuthoringWindow>("配置表生成");

        protected static T OpenWindow<T>(string title) where T : ConfigAuthoringWindow
        {
            var wasOpen = HasOpenInstances<T>();
            var window = GetWindow<T>(title);
            window.minSize = new Vector2(340, 160);
            if (!wasOpen) window.position = new Rect(100, 100, 420, 200);
            return window;
        }

        protected virtual void OnFocus() => _tasks = null;
        protected virtual void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                using (new EditorGUILayout.VerticalScope(ConfigAuthoringStyles.Page)) DrawContent();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
            finally { EditorGUILayout.EndScrollView(); }
        }

        // Projects inherit this window and provide their own MenuItem.
        protected virtual void DrawContent() => DrawDefaultContent();
        protected void DrawDefaultContent()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("配置表生成", ConfigAuthoringStyles.Title);
                if (GUILayout.Button("设置", GUILayout.Width(56), GUILayout.Height(24)))
                    SettingsService.OpenProjectSettings("Project/July/配置表");
            }
            GUILayout.Space(12);
            using (new EditorGUI.DisabledScope(!ConfigGeneration.CanGenerate))
            {
                if (Tasks.Count > 1)
                {
                    using (new EditorGUILayout.VerticalScope(ConfigAuthoringStyles.Card))
                        foreach (var group in Tasks.GroupBy(t => t.Group))
                        {
                            if (!string.IsNullOrEmpty(group.Key))
                                EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
                            foreach (var task in group)
                            {
                                var selected = EditorGUILayout.ToggleLeft(task.Name, _selected.Contains(task.Id), GUILayout.Height(24));
                                if (selected) _selected.Add(task.Id); else _selected.Remove(task.Id);
                            }
                        }
                    GUILayout.Space(10);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Tasks.Count == 1 ? "生成配置" : "生成全部", ConfigAuthoringStyles.Action, GUILayout.Height(36)))
                        ConfigGeneration.GenerateAll();
                    if (Tasks.Count > 1)
                        using (new EditorGUI.DisabledScope(_selected.Count == 0))
                            if (GUILayout.Button("生成选中", GUILayout.Height(36)))
                                ConfigGeneration.Generate(Tasks.Where(t => _selected.Contains(t.Id)).Select(t => t.Id).ToArray());
                }
            }

        }
    }
}
