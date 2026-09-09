using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace July.Release.Editor
{
    /// <summary>
    /// Player Build 时临时替换 TMP 默认字体为 Font_Launch，
    /// 避免 Font_Main / Font_Dynamic 通过 Resources 依赖链进入首包。
    /// AB Build 侧由 AssetBundleBuildStep 直接调用 TMPFontSwapper。
    /// </summary>
    public class TMPFontBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report) => TMPFontSwapper.SwapToLaunchFont();

        public void OnPostprocessBuild(BuildReport report) => TMPFontSwapper.RestoreFont();
    }
}
