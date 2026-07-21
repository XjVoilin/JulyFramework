using July.Platform;
using UnityEngine;

namespace July.Platform
{
    public struct DeviceInfoData
    {
        public string OS;
        public string Language;
        public string DeviceType;
        public int BenchmarkLevel;
    }

    public readonly struct ClipboardDataResultEvent
    {
        public readonly bool IsSuccess;
        public readonly string Data;

        public ClipboardDataResultEvent(bool isSuccess, string data = null)
        {
            IsSuccess = isSuccess;
            Data = data;
        }
    }

    public interface IDeviceService : IPlatformService
    {
        bool IsPc();
        int GetBenchmarkLevel();
        DeviceInfoData GetDeviceInfoData();
        void SetClipboardData(string data);
        void GetClipboardData();
        void ShowToast(string content, float duration = 1500f);
        Rect GetSafeArea();
        void VibrateShort(VibrateType type = VibrateType.Light);
        void VibrateLong();
    }
}

