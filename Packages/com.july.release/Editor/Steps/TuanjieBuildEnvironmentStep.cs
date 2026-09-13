using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>配置团结小游戏构建必需的环境，不修改项目的其他构建参数。</summary>
    public sealed class TuanjieBuildEnvironmentStep : BuildStep
    {
        public override string Name => "团结构建环境配置";

        public override string Validate(BuildContext context) => null;

        public override bool Execute(BuildContext context)
        {
#if TUANJIE_1_5_OR_NEWER
            if (context.Target != BuildTarget.MiniGame)
            {
                Debug.Log($"[Release] 当前目标为 {context.Target}，跳过团结小游戏环境配置。");
                return true;
            }

            // 已在团结 2022.3.61t8 核实；必须在 Generate All 生成 AOT 元数据之前设置。
            var previous = PlayerSettings.MiniGame.useSlimMetaFileFormat;
            PlayerSettings.MiniGame.useSlimMetaFileFormat = false;
            AssetDatabase.SaveAssets();
            var current = PlayerSettings.MiniGame.useSlimMetaFileFormat;
            Debug.Log($"[Release] 团结 {Application.unityVersion}，目标 {context.Target}，" +
                      $"useSlimMetaFileFormat: {previous} → {current}。");
            if (current)
            {
                Debug.LogError("[Release] 关闭精简元数据失败，停止构建。请检查团结构建设置。");
                return false;
            }
#else
            Debug.Log("[Release] 当前为国际版 Unity，跳过团结构建环境配置。");
#endif
            return true;
        }
    }
}
