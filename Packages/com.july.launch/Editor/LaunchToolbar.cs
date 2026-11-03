using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace July.Launch.Editor
{
    /// <summary>Unity 2022.3 主工具栏适配；内部 API 依赖集中于此。</summary>
    [InitializeOnLoad]
    internal static class LaunchToolbar
    {
        private const string ButtonName = "JulyLaunchSceneButton";
        private static readonly System.Type ToolbarType = typeof(UnityEditor.Editor).Assembly
            .GetType("UnityEditor.Toolbar", true);
        private static readonly FieldInfo ToolbarInstance = ToolbarType
            .GetField("get", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo ToolbarRoot = ToolbarType
            .GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);

        private static VisualElement _root;
        private static EditorToolbarButton _button;

        static LaunchToolbar()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Detach;
        }

        private static void Update()
        {
            var toolbar = ToolbarInstance.GetValue(null);
            if (toolbar == null) return;

            var root = (VisualElement)ToolbarRoot.GetValue(toolbar);
            if (root == null) return;
            if (root != _root)
            {
                // 原生工具栏先创建根节点，再组装播放控件；重建布局后重新挂接。
                var playMode = root.Q("PlayMode");
                if (playMode == null) return;

                DetachButton();
                root.Q(ButtonName)?.RemoveFromHierarchy();
                _button = new EditorToolbarButton(LaunchScenePlayer.Play)
                {
                    name = ButtonName,
                    text = "\u25b6 Launch",
                    tooltip = "检索 Assets 下唯一的 Launch.unity 并播放；退出后恢复原编辑场景。"
                };
                _button.style.marginRight = 6;
                _button.style.alignSelf = Align.Center;
                var parent = playMode.parent;
                parent.Insert(parent.IndexOf(playMode), _button);
                _root = root;
            }

            _button.SetEnabled(LaunchScenePlayer.CanPlay);
        }

        private static void Detach()
        {
            EditorApplication.update -= Update;
            DetachButton();
        }

        private static void DetachButton()
        {
            _button?.RemoveFromHierarchy();
            _button = null;
            _root = null;
        }
    }
}
