using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LitJson;
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
            window.minSize = new Vector2(320, 150);
            if (!wasOpen) window.position = new Rect(100, 100, 440, 160);
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
                GUILayout.Label("配置表生成", ConfigAuthoringStyles.Title);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("设置", EditorStyles.miniButton, GUILayout.Width(48), GUILayout.Height(22)))
                    SettingsService.OpenProjectSettings("Project/July/配置表");
            }

            GUILayout.Space(10);
            if (Tasks.Count > 1)
            {
                DrawTaskList();
                GUILayout.Space(8);
            }

            DrawGenerationActions();
            if (Tasks.Count == 1)
            {
                GUILayout.Space(10);
                DrawDirectories(Tasks[0]);
            }
        }

        private void DrawTaskList()
        {
            using (new EditorGUILayout.VerticalScope(ConfigAuthoringStyles.Card))
            {
                foreach (var group in Tasks.GroupBy(task => task.Group))
                {
                    if (!string.IsNullOrEmpty(group.Key))
                        GUILayout.Label(group.Key, EditorStyles.boldLabel);

                    foreach (var task in group)
                    {
                        using (new EditorGUI.DisabledScope(!ConfigGeneration.CanGenerate))
                        {
                            var selected = EditorGUILayout.ToggleLeft(task.Name, _selected.Contains(task.Id), GUILayout.Height(24));
                            if (selected) _selected.Add(task.Id); else _selected.Remove(task.Id);
                        }
                        DrawDirectories(task);
                        GUILayout.Space(6);
                    }
                }
            }
        }

        private void DrawGenerationActions()
        {
            using (new EditorGUI.DisabledScope(!ConfigGeneration.CanGenerate))
            using (new EditorGUILayout.HorizontalScope())
            {
                var background = GUI.backgroundColor;
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.55f, 0.79f, 1f) : new Color(0.71f, 0.86f, 1f);
                var generate = GUILayout.Button(Tasks.Count == 1 ? "生成配置" : "生成全部",
                    ConfigAuthoringStyles.Action, GUILayout.Height(32));
                GUI.backgroundColor = background;
                if (generate)
                    ConfigGeneration.GenerateAll();

                if (Tasks.Count > 1)
                    using (new EditorGUI.DisabledScope(_selected.Count == 0))
                        if (GUILayout.Button("生成选中", GUILayout.Height(32)))
                            ConfigGeneration.Generate(Tasks.Where(t => _selected.Contains(t.Id)).Select(t => t.Id).ToArray());
            }
        }

        private static void DrawDirectories(ConfigGenerationTask task)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开配置表目录", GUILayout.Height(25)))
                    OpenDirectory(GetTableDirectory(task));
                if (GUILayout.Button("打开 JSON 目录", GUILayout.Height(25)))
                    OpenDirectory(Path.Combine(ConfigGeneration.Project.Root, task.DataOutput));
            }
        }

        private static string GetTableDirectory(ConfigGenerationTask task)
        {
            var project = ConfigGeneration.Project;
            if (string.IsNullOrEmpty(task.ConfigFile))
                return Path.Combine(project.InputRoot, task.DataDirectory);

            var configFile = Path.Combine(project.Root, task.ConfigFile);
            var config = JsonMapper.ToObject(File.ReadAllText(configFile));
            return Path.Combine(Path.GetDirectoryName(configFile), (string)config["dataDir"]);
        }

        private static void OpenDirectory(string directory)
        {
            var path = Path.GetFullPath(directory);
            if (Directory.Exists(path))
                EditorUtility.OpenWithDefaultApp(path);
            else
                EditorUtility.DisplayDialog("目录不存在", path, "确定");
        }
    }
}
