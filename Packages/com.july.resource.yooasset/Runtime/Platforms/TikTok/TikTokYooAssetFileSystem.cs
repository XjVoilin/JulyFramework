#if UNITY_WEBGL && JULYGF_DY_MINIGAME
using System;
using YooAsset;

namespace July.Resource.YooAsset
{
    /// <summary>抖音小游戏 YooAsset 文件系统 Adapter。</summary>
    public static class TikTokYooAssetFileSystem
    {
        public static InitializeParameters CreateInitializeParameters(
            string mainUrl, string fallbackUrl = null, string packageRoot = "yoo")
        {
            if (string.IsNullOrWhiteSpace(mainUrl))
                throw new ArgumentException("抖音小游戏资源地址不能为空。", nameof(mainUrl));
            if (string.IsNullOrWhiteSpace(packageRoot))
                throw new ArgumentException("抖音小游戏缓存目录不能为空。", nameof(packageRoot));

            var remote = new RemoteServices(mainUrl, fallbackUrl);
            return new WebPlayModeParameters
            {
                WebServerFileSystemParameters =
                    TiktokFileSystemCreater.CreateFileSystemParameters(packageRoot, remote)
            };
        }

        private sealed class RemoteServices : IRemoteServices
        {
            private readonly string _main;
            private readonly string _fallback;

            public RemoteServices(string main, string fallback)
            {
                _main = main.TrimEnd('/');
                _fallback = string.IsNullOrWhiteSpace(fallback) ? _main : fallback.TrimEnd('/');
            }

            public string GetRemoteMainURL(string fileName) => $"{_main}/{fileName}";
            public string GetRemoteFallbackURL(string fileName) => $"{_fallback}/{fileName}";
        }
    }
}
#endif
