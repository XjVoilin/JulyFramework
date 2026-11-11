using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Config.Authoring
{
    public static class ConfigGeneration
    {
        public static bool IsGenerating { get; private set; }
        public static bool CanGenerate => !IsGenerating && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        public static ConfigGenerationProject Project => new ConfigGenerationProject(Path.GetDirectoryName(Application.dataPath));
        public static IReadOnlyList<ConfigGenerationTask> GetTasks()
        {
            var tasks = ConfigAuthoringSettings.instance.CreateScheme().CreateTasks(Project);
            foreach (var task in tasks) ConfigAuthoringSettings.instance.ApplyOutputs(task);
            return tasks;
        }
        public static ConfigGenerationResult GenerateAll() => Execute(null);
        public static ConfigGenerationResult Generate(params string[] taskIds) => Execute(taskIds);
        public static void GenerateAllForCI()
        {
            var result = GenerateAll();
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        }

        private static ConfigGenerationResult Execute(string[] ids)
        {
            if (!CanGenerate) return Report(new ConfigGenerationResult(false, "Cannot generate during compilation, Play Mode or another generation.", 0));
            IsGenerating = true;
            try
            {
                var tasks = GetTasks();
                ConfigGenerationRunner.ValidateTasks(Project.Root, tasks);
                if (ids != null)
                {
                    var selected = new HashSet<string>(ids, StringComparer.Ordinal);
                    var unknown = selected.Except(tasks.Select(t => t.Id)).ToArray();
                    if (unknown.Length != 0) throw new InvalidOperationException("Unknown configuration task: " + string.Join(", ", unknown));
                    tasks = tasks.Where(t => selected.Contains(t.Id)).ToArray();
                }
                ConfigGenerationResult result;
                EditorApplication.LockReloadAssemblies();
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                try
                {
                    result = new ConfigGenerationRunner(Project.Root).Run(tasks, (progress, task) =>
                        EditorUtility.DisplayProgressBar("配置表生成", task, progress));
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                    EditorUtility.ClearProgressBar();
                }
                Report(result);
                if (result.Succeeded) AssetDatabase.Refresh();
                return result;
            }
            catch (Exception error)
            {
                return Report(new ConfigGenerationResult(false, error.Message, 0));
            }
            finally { IsGenerating = false; }
        }

        private static ConfigGenerationResult Report(ConfigGenerationResult result)
        {
            if (result.Succeeded) Debug.Log($"[ConfigAuthoring] 配置表生成成功，共 {result.TaskCount} 项。");
            else Debug.LogError("[ConfigAuthoring] " + result.Error);
            return result;
        }
    }
}
