using July.Arch;
using July.Platform;
#if JULYGF_WX_MINIGAME
using UnityEngine;
using WeChatWASM;

namespace July.Platform
{
    internal sealed class WeChatDeviceService : IDeviceService, ICanEvent
    {
        private readonly DprConfig _dprConfig;
        private int _benchmarkLevel;
        private double _effectiveDpr;
        private DeviceInfoData _cachedInfo;
        private string _platform;

        internal WeChatDeviceService(DprConfig config)
        {
            _dprConfig = config;
        }

        public void Init()
        {
            var deviceInfo = WX.GetDeviceInfo();
            var wxBaseInfo = WX.GetAppBaseInfo();
            _benchmarkLevel = (int)deviceInfo.benchmarkLevel;
            _platform = deviceInfo.platform;

            _cachedInfo = new DeviceInfoData
            {
                OS = deviceInfo.system,
                Language = wxBaseInfo.language,
                DeviceType = _platform,
                BenchmarkLevel = _benchmarkLevel
            };

            _effectiveDpr = ApplyDpr(_benchmarkLevel);
        }

        public void DeferredInit()
        {
            var appBaseInfo = WX.GetAppBaseInfo();
            _cachedInfo.Language = appBaseInfo.language;
        }

        private double ApplyDpr(int benchmarkLevel)
        {
            var windowInfo = WX.GetWindowInfo();
            var defaultDpr = windowInfo.pixelRatio;
            if (_platform == "devtools")
            {
                Debug.Log(
                    $"[DPR] platform={_platform}, skipped=devtools, default={defaultDpr:F3}");
                return defaultDpr;
            }
            if (IsPc())
            {
                Debug.Log(
                    $"[DPR] platform={_platform}, skipped=pc, default={defaultDpr:F3}");
                return defaultDpr;
            }

            var tier = GetPerformanceTier(benchmarkLevel);
            var targetDpr = DevicePixelRatioPolicy.Calculate(
                _dprConfig, tier, defaultDpr, windowInfo.windowWidth, windowInfo.windowHeight);

            if (targetDpr < defaultDpr)
                WXBase.SetDevicePixelRatio(targetDpr);

            Debug.Log(
                $"[DPR] platform={_platform}, benchmarkLevel={benchmarkLevel}, tier={tier}, " +
                $"window={windowInfo.windowWidth}x{windowInfo.windowHeight}, default={defaultDpr:F3}, " +
                $"pixelBudgetEnabled={_dprConfig.EnablePixelBudget}, " +
                $"target={targetDpr:F3}, estimatedSize={(windowInfo.windowWidth * targetDpr):F0}x{(windowInfo.windowHeight * targetDpr):F0}");
            return targetDpr;
        }

        private DevicePerformanceTier GetPerformanceTier(int benchmarkLevel)
        {
            var isIOS = _platform == "ios";
            if (benchmarkLevel >= (isIOS ? 36 : 30))
                return DevicePerformanceTier.High;
            if (benchmarkLevel == -1 || benchmarkLevel >= (isIOS ? 30 : 23))
                return DevicePerformanceTier.Medium;
            return DevicePerformanceTier.Low;
        }

        public bool IsPc()
        {
            return _platform == "windows" || _platform == "mac";
        }

        public int GetBenchmarkLevel() => _benchmarkLevel;

        public DeviceInfoData GetDeviceInfoData() => _cachedInfo;

        public void SetClipboardData(string data)
        {
            Debug.Log($"[July.Platform.WeChat.Clipboard] Request length={data?.Length ?? 0}");

            var option = new SetClipboardDataOption
            {
                data = data,
                success = _ => Debug.Log("[July.Platform.WeChat.Clipboard] Success"),
                fail = result => Debug.LogError(
                    $"[July.Platform.WeChat.Clipboard] Failed: {result.errMsg}"),
            };
            WX.SetClipboardData(option);
        }

        public void GetClipboardData()
        {
            var option = new GetClipboardDataOption
            {
                success = result => this.Publish(new ClipboardDataResultEvent(true, result.data)),
                fail = _ => this.Publish(new ClipboardDataResultEvent(false)),
            };
            WX.GetClipboardData(option);
        }

        public void ShowToast(string content, float duration)
        {
            var option = new ShowToastOption
            {
                title = content,
                duration = duration,
            };
            WX.ShowToast(option);
        }

        public Rect GetSafeArea()
        {
            var info = WX.GetWindowInfo();
            var sa = info.safeArea;

            Debug.Log($"[SafeArea] Screen=({Screen.width}x{Screen.height}), " +
                      $"screen=({info.screenWidth}x{info.screenHeight}), " +
                      $"window=({info.windowWidth}x{info.windowHeight}), " +
                      $"pixelRatio={info.pixelRatio}, " +
                      $"safeArea=(left={sa.left}, top={sa.top}, right={sa.right}, bottom={sa.bottom}, " +
                      $"w={sa.width}, h={sa.height}), " +
                      $"Screen.safeArea={Screen.safeArea}");

            if (sa.width <= 0 || sa.height <= 0)
                return Screen.safeArea;

            var r = _effectiveDpr > 0 ? _effectiveDpr : info.pixelRatio;
            return new Rect(
                (float)(sa.left * r),
                (float)((info.screenHeight - sa.bottom) * r),
                (float)(sa.width * r),
                (float)(sa.height * r));
        }

        private static readonly string[] VibrateTypeNames = { "light", "medium", "heavy" };

        public void VibrateShort(VibrateType type = VibrateType.Light)
        {
            WX.VibrateShort(new VibrateShortOption { type = VibrateTypeNames[(int)type] });
        }

        public void VibrateLong()
        {
            WX.VibrateLong(new VibrateLongOption());
        }
    }
}
#endif
