using July.Release;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 构建工具窗口。本身只是容器，所有功能由可插拔的 <see cref="IBuildToolPanel"/> 提供。
    /// 增删功能只需在 <see cref="CreatePanels"/> 中注册/移除面板。
    /// </summary>
    public class BuildToolWindow : EditorWindow
    {
        BuildToolContext _ctx;
        IBuildToolPanel[] _panels;
        Vector2 _scrollPos;

        public static void ShowWindow()
        {
            var window = GetWindow<BuildToolWindow>("Build Tool");
            window.minSize = new Vector2(360, 420);
        }

        void OnEnable()
        {
            _ctx = new BuildToolContext
            {
                BootConfig = ReleaseProject.LoadBootConfig(),
                BuildConfig = ReleaseProject.LoadBuildConfig(),
                Repaint = Repaint,
            };

            _panels = CreatePanels();

            foreach (var panel in _panels)
                panel.OnEnable(_ctx);
        }

        /// <summary>
        /// 面板注册表。调整顺序即可改变显示顺序，注释掉即可移除功能。
        /// </summary>
        static IBuildToolPanel[] CreatePanels()
        {
            return new IBuildToolPanel[]
            {
                new PlatformPanel(),
                new VersionPanel(),
                new BuildPipelinePanel(),
                new ToolsPanel(),
                new HybridCLRPanel(),
                new DiffPanel(),
            };
        }

        void OnGUI()
        {
            if (_ctx.BootConfig == null || _ctx.BuildConfig == null)
            {
                if (_ctx.BootConfig == null)
                    EditorGUILayout.HelpBox(
                        $"未找到 BootConfig\n请通过 Create → JulyGF → Boot Config 创建并保存到:\n{BuildUtils.BootConfigPath}",
                        MessageType.Error);
                if (_ctx.BuildConfig == null)
                    EditorGUILayout.HelpBox(
                        $"未找到 BuildConfig\n请通过 Create → JulyGF → Build Config 创建并保存到:\n{BuildUtils.BuildConfigPath}",
                        MessageType.Error);
                if (GUILayout.Button("刷新"))
                {
                    _ctx.BootConfig = ReleaseProject.LoadBootConfig();
                    _ctx.BuildConfig = ReleaseProject.LoadBuildConfig();
                    foreach (var panel in _panels)
                        panel.OnEnable(_ctx);
                }

                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (var i = 0; i < _panels.Length; i++)
            {
                if (i > 0) EditorGUILayout.Space(8);
                _panels[i].OnGUI();
            }

            if (!string.IsNullOrEmpty(_ctx.StatusMessage))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_ctx.StatusMessage, _ctx.StatusType);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
