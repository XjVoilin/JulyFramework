using July.Arch;
using July.Platform;
#if JULYGF_WX_MINIGAME
using UnityEngine;
using WeChatWASM;

namespace July.Platform
{
    public class WeChatDeviceService : IDeviceService, ICanEvent
    {
        private int _benchmarkLevel;
        private double _effectiveDpr;
        private DeviceInfoData _cachedInfo;
        private string _platform;

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

            _effectiveDpr = ApplyDpr(_benchmarkLevel, _platform);
        }

        public void DeferredInit()
        {
            var appBaseInfo = WX.GetAppBaseInfo();
            _cachedInfo.Language = appBaseInfo.language;
        }

        private static double ApplyDpr(int benchmarkLevel, string platform)
        {
            var isIOS = platform == "ios";
            var highThreshold = isIOS ? 36 : 30;
            var midThreshold = isIOS ? 30 : 23;
            var defaultDpr = WX.GetWindowInfo().pixelRatio;

            if (benchmarkLevel >= highThreshold)
            {
                Debug.Log($"[DPR] 高端设备 (platform={platform}, benchmarkLevel={benchmarkLevel})，保持默认 DPR");
                return defaultDpr;
            }

            var scale = benchmarkLevel >= midThreshold || benchmarkLevel == -1 ? 0.7 : 0.5;
            var targetDpr = defaultDpr * scale;
            Debug.Log($"[DPR] platform={platform}, benchmarkLevel={benchmarkLevel}, 默认DPR={defaultDpr}, 缩放={scale}, 目标DPR={targetDpr}");
            WXBase.SetDevicePixelRatio(targetDpr);
            return targetDpr;
        }

        public bool IsPc()
        {
            return _platform == "windows" || _platform == "mac";
        }

        public int GetBenchmarkLevel() => _benchmarkLevel;

        public DeviceInfoData GetDeviceInfoData() => _cachedInfo;

        public void SetClipboardData(string data)
        {
            var option = new SetClipboardDataOption
            {
                data = data,
                success = _ => { },
                fail = _ => { },
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

