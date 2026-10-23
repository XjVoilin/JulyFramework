using System;
using July.Arch;
using July.Platform;
#if JULYGF_DY_MINIGAME
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    internal sealed class TikTokDeviceService : IDeviceService, ICanEvent
    {
        private readonly DprConfig _dprConfig;
        private double _effectiveDpr;
        private DeviceInfoData _cachedInfo;
        private string _platform;

        internal TikTokDeviceService(DprConfig config)
        {
            _dprConfig = config;
        }

        public void Init()
        {
            var sysInfo = TT.GetSystemInfo();
            var score = sysInfo.deviceScore.overall;
            _platform = sysInfo.platform;

            _cachedInfo = new DeviceInfoData
            {
                OS = sysInfo.system,
                Language = sysInfo.language,
                DeviceType = _platform,
                BenchmarkLevel = (int)score
            };

            _effectiveDpr = ApplyDpr(
                score,
                sysInfo.pixelRatio,
                sysInfo.screenWidth,
                sysInfo.screenHeight);
            Debug.Log($"[Device] deviceScore: cpu={sysInfo.deviceScore.cpu}, gpu={sysInfo.deviceScore.gpu}, " +
                      $"memory={sysInfo.deviceScore.memory}, overall={score}");
        }

        private double ApplyDpr(
            double overall,
            double defaultDpr,
            double logicalWidth,
            double logicalHeight)
        {
            if (IsPc())
            {
                Debug.Log(
                    $"[DPR] platform={_platform}, skipped=pc, default={defaultDpr:F3}");
                return defaultDpr;
            }

            var tier = GetPerformanceTier(overall);
            var targetDpr = DevicePixelRatioPolicy.Calculate(
                _dprConfig, tier, defaultDpr, logicalWidth, logicalHeight);

            if (targetDpr < defaultDpr)
            {
                targetDpr = Math.Floor(targetDpr * 100d) / 100d;
                TT.SetPreferredDevicePixelRatio((float)targetDpr);
            }

            Debug.Log(
                $"[DPR] platform={_platform}, overall={overall}, tier={tier}, " +
                $"window={logicalWidth}x{logicalHeight}, default={defaultDpr:F3}, " +
                $"pixelBudgetEnabled={_dprConfig.EnablePixelBudget}, " +
                $"target={targetDpr:F3}, estimatedSize={(logicalWidth * targetDpr):F0}x{(logicalHeight * targetDpr):F0}");
            return targetDpr;
        }

        private static DevicePerformanceTier GetPerformanceTier(double overall)
        {
            if (overall >= 8.51)
                return DevicePerformanceTier.High;
            if (overall < 0d || overall >= 7.30)
                return DevicePerformanceTier.Medium;
            return DevicePerformanceTier.Low;
        }

        public bool IsPc()
        {
            return _platform == "windows" || _platform == "mac";
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

