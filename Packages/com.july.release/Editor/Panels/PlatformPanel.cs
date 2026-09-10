using static July.Release.Editor.PlatformPreparation;
using System;
using System.Collections.Generic;
using System.Linq;
using July.Release;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Release.Editor
{
    public sealed class PlatformPanel : IBuildToolPanel
    {
        const string PrefKeyDebug = "BuildTool_DebugBuild";

        static readonly string[] EnvLabels = { "Dev", "Test", "Prod" };


        BuildToolContext _ctx;
        int _platformIndex;
        int _envIndex;

        public void OnEnable(BuildToolContext ctx)
        {
            _ctx = ctx;
            _platformIndex = System.Array.IndexOf(PlatformKeys.Options, EditorPlatformPref.Platform);
            if (_platformIndex < 0) _platformIndex = 0;
            _ctx.DebugBuild = ProjectEditorPrefs.GetBool(PrefKeyDebug, true);
            _envIndex = (int)_ctx.BootConfig.env;
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("平台环境", EditorStyles.boldLabel);

            var (currentPlatform, currentDebug) = DetectCurrentDefines();
            var currentTarget = EditorUserBuildSettings.activeBuildTarget;
            var currentEnv = _ctx.BootConfig != null ? _ctx.BootConfig.env : ReleaseEnvironment.Dev;

            var envLabel = currentTarget.ToString();
            if (currentPlatform != null) envLabel += $" · {currentPlatform}";
            envLabel += $" · {currentEnv}";
            if (currentDebug) envLabel += " · Debug";
            EditorGUILayout.LabelField("当前环境", envLabel);

            EditorGUILayout.BeginHorizontal();

            _platformIndex = Array.IndexOf(PlatformKeys.Options, _ctx.CurrentPlatform);
            EditorGUI.BeginChangeCheck();
            _platformIndex = EditorGUILayout.Popup("目标平台", _platformIndex, PlatformKeys.Options);
            if (EditorGUI.EndChangeCheck())
                EditorPlatformPref.Platform = PlatformKeys.Options[_platformIndex];

            _envIndex = (int)_ctx.BootConfig.env;
            EditorGUI.BeginChangeCheck();
            _envIndex = EditorGUILayout.Popup(_envIndex, EnvLabels, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_ctx.BootConfig.Asset, "修改构建环境");
                _ctx.BootConfig.env = (ReleaseEnvironment)_envIndex;
                EditorUtility.SetDirty(_ctx.BootConfig.Asset);
                AssetDatabase.SaveAssetIfDirty(_ctx.BootConfig.Asset);
            }

            EditorGUI.BeginChangeCheck();
            _ctx.DebugBuild = EditorGUILayout.ToggleLeft("Debug", _ctx.DebugBuild, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
                ProjectEditorPrefs.SetBool(PrefKeyDebug, _ctx.DebugBuild);

            EditorGUILayout.EndHorizontal();

            var targetPlatform = _ctx.CurrentPlatform;
            var needsSwitchDefines = currentTarget != PlatformBuildTarget
                                    || !AreDefinesCurrent(targetPlatform, _ctx.DebugBuild);

            if (needsSwitchDefines)
            {
                EditorGUILayout.HelpBox("当前编译宏与目标不匹配，请先切换平台", MessageType.Warning);
                if (GUILayout.Button("应用平台与 Debug 设置", GUILayout.Height(26)))
                    SwitchPlatform(targetPlatform);
            }

        }

        static (string platform, bool isDebug) DetectCurrentDefines()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(PlatformBuildTargetGroup);
            string platform = null;
            foreach (var kvp in PlatformDefineMap)
            {
                if (defines.Contains(kvp.Value[0]))
                {
                    platform = kvp.Key;
                    break;
                }
            }

            return (platform, defines.Contains("JULYGF_DEBUG"));
        }

        void SwitchPlatform(string platform)
        {
            if (!EditorUtility.DisplayDialog("切换平台",
                    $"目标: {platform}{(_ctx.DebugBuild ? " · Debug" : "")}\n\n" +
                    "将自动设置:\n" +
                    $"• Build Target → {PlatformBuildTargetLabel}\n" +
                    "• Scripting Define Symbols\n" +
                    "• Graphics API → OpenGLES3\n" +
                    "• Scripting Backend → IL2CPP\n\n" +
                    "切换后引擎会重新编译，请耐心等待。\n" +
                    "（环境切换不需要重新编译，由 BootConfig 控制）",
                    "确认切换", "取消"))
                return;

            PlatformPreparation.Apply(platform, _ctx.DebugBuild);
        }
    }
}
