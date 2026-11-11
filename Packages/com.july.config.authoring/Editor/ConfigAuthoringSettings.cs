using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Config.Authoring
{
    [FilePath("ProjectSettings/July/ConfigAuthoring/settings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ConfigAuthoringSettings : ScriptableSingleton<ConfigAuthoringSettings>
    {
        [SerializeField] private string _schemeType = "";
        [SerializeField] private List<ConfigOutputOverride> _outputs = new List<ConfigOutputOverride>();

        // Settings store a plain C# type, never a project ScriptableObject asset.
        internal IConfigGenerationScheme CreateScheme()
        {
            if (string.IsNullOrEmpty(_schemeType)) return new DefaultConfigGenerationScheme();
            var type = SchemeTypes().SingleOrDefault(t => t.AssemblyQualifiedName == _schemeType);
            if (type == null) throw new InvalidOperationException("配置表生成方案不存在：" + _schemeType);
            return (IConfigGenerationScheme)Activator.CreateInstance(type);
        }
        private static Type[] SchemeTypes() => TypeCache.GetTypesDerivedFrom<IConfigGenerationScheme>()
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters &&
                !typeof(ScriptableObject).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();

        private void SetOutputs(string taskId, string code, string data)
        {
            var entry = _outputs.SingleOrDefault(o => o.TaskId == taskId);
            if (entry == null) { entry = new ConfigOutputOverride { TaskId = taskId }; _outputs.Add(entry); }
            entry.CodeDirectory = code; entry.DataDirectory = data;
            Save(true);
        }
        internal void ApplyOutputs(ConfigGenerationTask task)
        {
            var entry = _outputs.SingleOrDefault(o => o.TaskId == task.Id);
            if (entry == null) return;
            task.CodeOutput = entry.CodeDirectory; task.DataOutput = entry.DataDirectory;
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Project/July/配置表", SettingsScope.Project)
        {
            label = "配置表",
            guiHandler = _ => instance.DrawSettings()
        };
        private void DrawSettings()
        {
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 112;
            try
            {
                using (new EditorGUILayout.VerticalScope(ConfigAuthoringStyles.Page))
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(900)))
                    using (new EditorGUI.DisabledScope(!ConfigGeneration.CanGenerate))
                    {
                        EditorGUILayout.LabelField("通用配置", ConfigAuthoringStyles.Title);
                        GUILayout.Space(8);
                        using (new EditorGUILayout.VerticalScope(ConfigAuthoringStyles.Card))
                        {
                            var types = SchemeTypes().Where(t => t != typeof(DefaultConfigGenerationScheme)).ToArray();
                            var names = new[] { "默认方案" }.Concat(types.Select(t => t.FullName)).ToArray();
                            var values = new[] { "" }.Concat(types.Select(t => t.AssemblyQualifiedName)).ToArray();
                            var current = Array.IndexOf(values, _schemeType);
                            EditorGUI.BeginChangeCheck();
                            var selected = EditorGUILayout.Popup("生成方案", current, names);
                            if (EditorGUI.EndChangeCheck()) { _schemeType = values[selected]; Save(true); }
                            GUILayout.Space(4);
                            ConfigAuthoringStyles.ReadOnlyField("固定输入目录", ConfigGeneration.Project.InputRoot);
                        }
                        GUILayout.Space(18);
                        try
                        {
                            foreach (var task in ConfigGeneration.GetTasks())
                            {
                                EditorGUILayout.LabelField(task.Name, ConfigAuthoringStyles.Title);
                                GUILayout.Space(8);
                                using (new EditorGUILayout.VerticalScope(ConfigAuthoringStyles.Card))
                                {
                                    EditorGUILayout.LabelField("固定输入", EditorStyles.miniBoldLabel);
                                    GUILayout.Space(4);
                                    ConfigAuthoringStyles.ReadOnlyField("输入", task.ConfigFile ?? task.DataDirectory);
                                    GUILayout.Space(4);
                                    ConfigAuthoringStyles.ReadOnlyField("命名空间", task.Namespace);
                                    GUILayout.Space(12);
                                    EditorGUILayout.LabelField("输出", EditorStyles.miniBoldLabel);
                                    GUILayout.Space(4);
                                    EditorGUI.BeginChangeCheck();
                                    var code = EditorGUILayout.TextField("C# 输出目录", task.CodeOutput);
                                    GUILayout.Space(4);
                                    var data = EditorGUILayout.TextField("JSON 输出目录", task.DataOutput);
                                    if (EditorGUI.EndChangeCheck()) SetOutputs(task.Id, code, data);
                                }
                                GUILayout.Space(18);
                            }
                        }
                        catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; }
        }
    }
    [Serializable]
    internal sealed class ConfigOutputOverride
    {
        public string TaskId, CodeDirectory, DataDirectory;
    }
}
