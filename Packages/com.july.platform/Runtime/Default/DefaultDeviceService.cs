using July.Arch;
using July.Platform;
using UnityEngine;

namespace July.Platform
{
    public class DefaultDeviceService : IDeviceService, ICanEvent
    {
        public bool IsPc() => true;

        public int GetBenchmarkLevel() => -1;

        public DeviceInfoData GetDeviceInfoData() => new()
        {
            OS = SystemInfo.operatingSystem,
            Language = Application.systemLanguage.ToString(),
            DeviceType = SystemInfo.deviceType.ToString(),
            BenchmarkLevel = -1
        };

        public void SetClipboardData(string data)
        {
            GUIUtility.systemCopyBuffer = data;
        }

        public void GetClipboardData()
        {
            this.Publish(new ClipboardDataResultEvent(true, GUIUtility.systemCopyBuffer));
        }

        public void ShowToast(string content, float duration) { }

        public Rect GetSafeArea() => Screen.safeArea;

        public void VibrateShort(VibrateType type = VibrateType.Light) { }

        public void VibrateLong() { }
    }
}

