using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class ToolsPanel : IBuildToolPanel
    {
        BuildToolContext _ctx;

        public void OnEnable(BuildToolContext ctx)
        {
            _ctx = ctx;
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开 CDN 目录"))
                OpenCDNFolder();
            if (GUILayout.Button("清理 CDN"))
                CleanCDN();
            EditorGUILayout.EndHorizontal();
        }

        void OpenCDNFolder()
        {
            var dir = Path.GetFullPath($"{BuildUtils.CdnRoot}/{_ctx.CurrentPlatform}");
            if (Directory.Exists(dir))
            {
                EditorUtility.RevealInFinder(dir);
            }
            else
            {
                dir = Path.GetFullPath(BuildUtils.CdnRoot);
                if (Directory.Exists(dir))
                    EditorUtility.RevealInFinder(dir);
                else
                    _ctx.SetStatus("本地 CDN 目录不存在，请先构建", MessageType.Warning);
            }
        }

        void CleanCDN()
        {
            var platform = _ctx.CurrentPlatform;
            var dir = Path.GetFullPath($"{BuildUtils.CdnRoot}/{platform}");
            if (!Directory.Exists(dir))
            {
                _ctx.SetStatus($"{platform} 本地 CDN 目录不存在，无需清理", MessageType.Info);
                return;
            }

            var fileCount = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            if (!EditorUtility.DisplayDialog("清理 CDN",
                    $"将删除 {platform} 的全部本地 CDN 缓存\n\n" +
                    $"目录: {dir}\n" +
                    $"文件数: {fileCount}\n\n" +
                    "此操作不影响远端 COS，仅清理本地。",
                    "确认删除", "取消"))
                return;

            Directory.Delete(dir, true);
            AssetDatabase.Refresh();
            _ctx.SetStatus($"{platform} 本地 CDN 已清理", MessageType.Info);
            Debug.Log($"[BuildTool] 已删除: {dir}");
        }
    }
}
