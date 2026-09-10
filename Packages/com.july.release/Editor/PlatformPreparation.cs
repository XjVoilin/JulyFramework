using System;
using System.Collections.Generic;
using System.Linq;
using July.Release;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Release.Editor
{
    /// <summary>窗口与 CI 共用的平台设置；宏变更后需要重新编译，再执行构建。</summary>
    public static class PlatformPreparation
    {
        internal static string[] BaseDefines => ReleaseProject.LoadBuildConfig().baseDefines;

        internal static readonly string[] DebugDefines = { "JULYGF_DEBUG" };

        internal static readonly Dictionary<string, string[]> PlatformDefineMap = new()
        {
            { PlatformKeys.WeChat, new[] { "JULYGF_WX_MINIGAME" } },
            { PlatformKeys.TikTok, CreateTikTokDefines() },
        };

        static string[] CreateTikTokDefines()
        {
            var defines = new List<string> { "JULYGF_DY_MINIGAME" };
#if TUANJIE_1_5_OR_NEWER
            // BuildForTuanjie 会注入该宏。提前纳入标准集合，确保 HybridCLR、
            // AOT hash 和最终小游戏主包始终使用同一编译环境。
            defines.Add("TTSDK_MIX_ENGINE");
#endif
            return defines.ToArray();
        }

        internal static string[] GetExpectedDefines(string platform, bool debug)
        {
            var defines = new List<string>(BaseDefines);
            if (PlatformDefineMap.TryGetValue(platform, out var platformDefines))
                defines.AddRange(platformDefines);
            if (debug)
                defines.AddRange(DebugDefines);

            return defines
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        internal static bool AreDefinesCurrent(string platform, bool debug)
        {
            var current = GetCurrentDefines();
            var expected = new HashSet<string>(
                GetExpectedDefines(platform, debug), StringComparer.Ordinal);
            return current.SetEquals(expected);
        }

        internal static string DescribeDefineMismatch(string platform, bool debug)
        {
            var current = GetCurrentDefines();
            var expected = new HashSet<string>(
                GetExpectedDefines(platform, debug), StringComparer.Ordinal);

            if (current.SetEquals(expected))
                return null;

            var missing = expected.Except(current).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var extra = current.Except(expected).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var details = new List<string>();
            if (missing.Length > 0)
                details.Add($"缺少: {string.Join(", ", missing)}");
            if (extra.Length > 0)
                details.Add($"多余: {string.Join(", ", extra)}");
            return string.Join("；", details);
        }

        static HashSet<string> GetCurrentDefines()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(PlatformBuildTargetGroup);
            return new HashSet<string>(
                defines.Split(';').Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
        }


#if TUANJIE_1_5_OR_NEWER
        internal static BuildTarget PlatformBuildTarget => BuildTarget.MiniGame;
        internal static BuildTargetGroup PlatformBuildTargetGroup => BuildTargetGroup.MiniGame;
        internal const string PlatformBuildTargetLabel = "MiniGame";
#else
        internal static BuildTarget PlatformBuildTarget => BuildTarget.WebGL;
        internal static BuildTargetGroup PlatformBuildTargetGroup => BuildTargetGroup.WebGL;
        internal const string PlatformBuildTargetLabel = "WebGL";
#endif
        public static void Apply(string platform, bool debug)
        {
            if (!PlatformDefineMap.ContainsKey(platform))
                throw new ArgumentException($"Unsupported platform: {platform}");
            var defines = GetExpectedDefines(platform, debug);
            if (!AreDefinesCurrent(platform, debug))
                PlayerSettings.SetScriptingDefineSymbolsForGroup(PlatformBuildTargetGroup, string.Join(";", defines));
            PlayerSettings.SetUseDefaultGraphicsAPIs(PlatformBuildTarget, false);
            PlayerSettings.SetGraphicsAPIs(PlatformBuildTarget, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetScriptingBackend(PlatformBuildTargetGroup, ScriptingImplementation.IL2CPP);
            EditorPlatformPref.Platform = platform;
            if (EditorUserBuildSettings.activeBuildTarget != PlatformBuildTarget &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(PlatformBuildTargetGroup, PlatformBuildTarget))
                throw new InvalidOperationException($"Cannot switch build target to {PlatformBuildTarget}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Release] Platform ready: {platform}, debug={debug}, target={PlatformBuildTarget}");
        }
    }
}
