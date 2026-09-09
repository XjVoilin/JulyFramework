using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 构建时强制关闭 Splash Screen 的 Unity Logo，
    /// 避免引擎内置 Logo 纹理通过 PlayerSettings 依赖链进入首场景资源包。
    /// </summary>
    public class SplashScreenBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ReleaseProject.Profile.DisableUnitySplash) return;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            Debug.Log("[SplashScreenBuildProcessor] Pre-build: disabled SplashScreen & Unity Logo");
        }
    }
}
