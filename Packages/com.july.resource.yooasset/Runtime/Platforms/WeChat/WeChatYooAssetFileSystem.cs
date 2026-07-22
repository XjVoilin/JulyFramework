#if UNITY_WEBGL && JULYGF_WX_MINIGAME
using System;
using YooAsset;
using WeChatWASM;

namespace July.Resource.YooAsset
{
    /// <summary>微信小游戏 YooAsset 文件系统 Adapter。</summary>
    public static class WeChatYooAssetFileSystem
    {
        public static InitializeParameters CreateInitializeParameters(
            string mainUrl, string fallbackUrl = null)
        {
            if (string.IsNullOrWhiteSpace(mainUrl))
                throw new ArgumentException("微信小游戏资源地址不能为空。", nameof(mainUrl));

            var cdnUri = new Uri(mainUrl);
            WX.SetDataCDN($"{cdnUri.Scheme}://{cdnUri.Authority}/");
            var packageRoot = $"{WX.env.USER_DATA_PATH}/__GAME_FILE_CACHE{cdnUri.AbsolutePath}";
            var remote = new RemoteServices(mainUrl, fallbackUrl);

            return new WebPlayModeParameters
            {
                WebServerFileSystemParameters =
                    WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remote)
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
