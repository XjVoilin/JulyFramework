using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    internal static class ProjectRenderConfiguration
    {
#if TUANJIE_1_5_OR_NEWER
        internal const BuildTarget Target = BuildTarget.MiniGame;
        internal const string PlatformName = "MiniGame";
#else
        internal const BuildTarget Target = BuildTarget.WebGL;
        internal const string PlatformName = "WebGL";
#endif
        internal static int[] QualityLevels => QualitySettings.GetActiveQualityLevelsForPlatform(PlatformName);
        internal static Object LoadSettings(string path) => AssetDatabase.LoadAllAssetsAtPath(path).Single();

        internal static IEnumerable<RenderPipelineAsset> Pipelines()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
                yield return GraphicsSettings.defaultRenderPipeline;
            foreach (var level in QualityLevels)
            {
                var asset = QualitySettings.GetRenderPipelineAssetAt(level);
                if (asset != null)
                    yield return asset;
            }
        }

        internal static bool IsUrp(Object asset) =>
            asset.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
    }
}
