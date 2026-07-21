using July.Arch;
using July.Platform;
#if JULYGF_DY_MINIGAME
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public class TikTokDeviceService : IDeviceService, ICanEvent
    {
        private double _effectiveDpr;
        private DeviceInfoData _cachedInfo;

        public void Init()
        {
            var sysInfo = TT.GetSystemInfo();
            var score = sysInfo.deviceScore.overall;

            _cachedInfo = new DeviceInfoData
            {
                OS = sysInfo.system,
                Language = sysInfo.language,
                DeviceType = sysInfo.platform,
                BenchmarkLevel = (int)score
            };

            _effectiveDpr = ApplyDpr(score, sysInfo.pixelRatio);
            Debug.Log($"[Device] deviceScore: cpu={sysInfo.deviceScore.cpu}, gpu={sysInfo.deviceScore.gpu}, " +
                      $"memory={sysInfo.deviceScore.memory}, overall={score}");
        }

        private static double ApplyDpr(double overall, double defaultDpr)
        {
            const double highThreshold = 8.51;
            const double midThreshold = 7.30;

            if (overall >= highThreshold)
            {
                Debug.Log($"[DPR] 高端设备 (overall={overall})，保持默认 DPR");
                return defaultDpr;
            }

            var scale = overall >= midThreshold || overall < 0 ? 0.7 : 0.5;
            var targetDpr = defaultDpr * scale;
            Debug.Log($"[DPR] overall={overall}, 默认DPR={defaultDpr}, 缩放={scale}, 目标DPR={targetDpr}");
            TT.SetPreferredDevicePixelRatio((float)targetDpr);
            return targetDpr;
        }

        public bool IsPc()
        {
            var platform = TT.GetSystemInfo().platform;
            return platform == "windows" || platform == "mac";
        }

        public int GetBenchmarkLevel() => _cachedInfo.BenchmarkLevel;

        public DeviceInfoData GetDeviceInfoData() => _cachedInfo;

        public void SetClipboardData(string data)
        {
            TT.SetClipboardData(data, (_, _) => { });
        }

        public void GetClipboardData()
        {
            TT.GetClipboardData((success, data) =>
                this.Publish(new ClipboardDataResultEvent(success, data)));
        }

        public void ShowToast(string content, float duration)
        {
            var param = new TTShowToastParam
            {
                title = content,
                duration = Mathf.CeilToInt(duration)
            };
            TT.ShowToast(param);
        }

        public Rect GetSafeArea()
        {
            var info = TT.GetSystemInfo();
            var sa = info.safeArea;
            if (sa.width > 0 && sa.height > 0)
            {
                var r = _effectiveDpr;
                return new Rect(
                    (float)(sa.left * r),
                    (float)((info.screenHeight - sa.bottom) * r),
                    (float)(sa.width * r),
                    (float)(sa.height * r));
            }
            return Screen.safeArea;
        }

        public void VibrateShort(VibrateType type = VibrateType.Light)
        {
            TT.VibrateShort(new VibrateShortParam());
        }

        public void VibrateLong()
        {
            TT.VibrateLong(new VibrateLongParam());
        }
    }
}
#endif

