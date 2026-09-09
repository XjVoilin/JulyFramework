using System;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 同步小游戏构建的 Debug Symbol 设置。仅对 <see cref="PlatformKeys.WeChat"/> /
    /// <see cref="PlatformKeys.TikTok"/> 生效，非小游戏平台直接跳过。
    /// <para>
    /// 规则：以当前小游戏平台 ScriptingDefineSymbols 中是否包含 <c>JULYGF_DEBUG</c> 为准
    /// （PlatformPanel 切换 Debug 时会自动加/减该宏）。
    /// </para>
    /// <list type="bullet">
    ///   <item><description>JULYGF_DEBUG 开启 → <b>External</b>：生成独立 <c>*.symbols.json</c>，便于线上崩溃回溯调用栈。</description></item>
    ///   <item><description>JULYGF_DEBUG 关闭 → <b>Off</b>：不生成符号表，wasm 体积最小，发版包用。</description></item>
    /// </list>
    /// <para>
    /// 国际版 Unity 与团结引擎在该 API 上不兼容，按 <c>TUANJIE_1_5_OR_NEWER</c> 宏分两套实现：
    /// </para>
    /// <list type="table">
    ///   <listheader><term>引擎</term><description>BuildTargetGroup / PlayerSettings 入口 / 枚举</description></listheader>
    ///   <item><term>国际版 Unity</term><description><c>WebGL</c> / <c>PlayerSettings.WebGL.debugSymbolMode</c> / <c>WebGLDebugSymbolMode</c></description></item>
    ///   <item><term>团结引擎</term><description><c>MiniGame</c>（同义于 <c>WeixinMiniGame</c>） / <c>PlayerSettings.WeixinMiniGame.debugSymbolMode</c> / <c>WeixinMiniGameDebugSymbolMode</c></description></item>
    /// </list>
    /// </summary>
    public sealed class WebGLDebugSymbolStep : BuildStep
    {
        public override string Name => "WebGL Debug Symbol 同步";

        const string DebugMacro = "JULYGF_DEBUG";
        const string Sentinel = "[DebugSymbol]";

        public override string Validate(BuildContext ctx) => null;

        public override bool Execute(BuildContext ctx)
        {
            if (ctx.Platform != PlatformKeys.WeChat && ctx.Platform != PlatformKeys.TikTok)
            {
                Debug.Log($"{Sentinel} 平台 {ctx.Platform} 非小游戏，跳过");
                return true;
            }

            var hasDebug = HasDebugMacro();
            ApplyDebugSymbolMode(hasDebug);
            return true;
        }

        static bool HasDebugMacro()
        {
#if TUANJIE_1_5_OR_NEWER
            var group = BuildTargetGroup.MiniGame;
#else
            var group = BuildTargetGroup.WebGL;
#endif
            var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            if (string.IsNullOrEmpty(symbols)) return false;

            foreach (var token in symbols.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Trim() == DebugMacro) return true;
            }
            return false;
        }

        static void ApplyDebugSymbolMode(bool hasDebug)
        {
            var macroState = hasDebug ? "ON" : "OFF";

#if TUANJIE_1_5_OR_NEWER
            var desired = hasDebug
                ? WeixinMiniGameDebugSymbolMode.External
                : WeixinMiniGameDebugSymbolMode.Off;
            var current = PlayerSettings.WeixinMiniGame.debugSymbolMode;

            if (current == desired)
            {
                Debug.Log($"{Sentinel} (Tuanjie) 已是 {desired}（{DebugMacro}={macroState}），跳过");
                return;
            }

            PlayerSettings.WeixinMiniGame.debugSymbolMode = desired;
            AssetDatabase.SaveAssets();
            Debug.Log($"{Sentinel} (Tuanjie) {current} → {desired}（{DebugMacro}={macroState}）");
#else
            var desired = hasDebug
                ? WebGLDebugSymbolMode.External
                : WebGLDebugSymbolMode.Off;
            var current = PlayerSettings.WebGL.debugSymbolMode;

            if (current == desired)
            {
                Debug.Log($"{Sentinel} (Unity) 已是 {desired}（{DebugMacro}={macroState}），跳过");
                return;
            }

            PlayerSettings.WebGL.debugSymbolMode = desired;
            AssetDatabase.SaveAssets();
            Debug.Log($"{Sentinel} (Unity) {current} → {desired}（{DebugMacro}={macroState}）");
#endif
        }
    }
}
