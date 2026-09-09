using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class ToolsPanel : IBuildToolPanel
    {
        BuildToolContext _ctx;
        public void OnEnable(BuildToolContext context) => _ctx = context;

        public void OnGUI()
        {
            EditorGUILayout.LabelField("本地资源目录", EditorStyles.boldLabel);
            var context = _ctx.Preview;
            var validVersion = BuildUtils.IsValidVersion(context.CoreVersion) && BuildUtils.IsValidVersion(context.PlanVersion);
            using (new EditorGUI.DisabledScope(!validVersion))
            {
                var directory = validVersion ? BuildUtils.GetCdnPath(context.Env, context.Platform, context.CoreVersion, context.PlanVersion) : null;
                EditorGUILayout.LabelField("当前版本", directory ?? "请先选择有效版本", EditorStyles.wordWrappedLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("打开当前版本目录"))
                    {
                        if (Directory.Exists(directory)) EditorUtility.RevealInFinder(directory);
                        else _ctx.SetStatus("当前版本尚无本地资源产物", MessageType.Info);
                    }
                    if (GUILayout.Button("清理当前版本")) CleanDirectory(directory);
                }
            }
        }

        void CleanDirectory(string directory)
        {
            var root = Path.GetFullPath(BuildUtils.CdnRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var target = Path.GetFullPath(directory);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("清理目标必须位于配置的本地 CDN 根目录内。");
            if (!Directory.Exists(target)) { _ctx.SetStatus("当前版本目录不存在，无需清理", MessageType.Info); return; }
            var count = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length;
            if (!EditorUtility.DisplayDialog("清理当前版本本地资源",
                    $"将删除以下目录及其中 {count} 个文件：\n{target}\n\n其他本地版本与远端 COS 不受影响。",
                    "删除当前版本", "取消")) return;
            Directory.Delete(target, true);
            _ctx.SetStatus($"已清理本地目录：{target}", MessageType.Info);
        }
    }
}
