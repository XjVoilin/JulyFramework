using System;

namespace July.Persistence
{
    public enum SaveFailureReason
    {
        None = 0,
        Unknown = 1,
        DiskFull = 2,
        PermissionDenied = 3,
        FileInUse = 4,
        DeviceError = 5,
        SerializationFailed = 6,
        EncryptionFailed = 7,
        InvalidData = 8,
        Cancelled = 9
    }

    public enum SaveImportance
    {
        Critical = 0,
        Important = 1,
        Normal = 2,
        Trivial = 3
    }

    public enum SaveSignal
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Immediate = 3
    }

    public struct SaveResult
    {
        public bool Success { get; private set; }
        public SaveFailureReason FailureReason { get; private set; }
        public string FailureMessage { get; private set; }

        public static SaveResult CreateSuccess()
        {
            return new SaveResult
            {
                Success = true,
                FailureReason = SaveFailureReason.None,
                FailureMessage = string.Empty
            };
        }

        public static SaveResult CreateFailure(SaveFailureReason reason, string message = null)
        {
            return new SaveResult
            {
                Success = false,
                FailureReason = reason,
                FailureMessage = message ?? GetDefaultFailureMessage(reason)
            };
        }

        private static string GetDefaultFailureMessage(SaveFailureReason reason)
        {
            return reason switch
            {
                SaveFailureReason.DiskFull => "磁盘空间不足，无法保存游戏数据",
                SaveFailureReason.PermissionDenied => "没有写入权限，无法保存游戏数据",
                SaveFailureReason.FileInUse => "存档文件被占用，请稍后重试",
                SaveFailureReason.DeviceError => "设备异常，无法保存游戏数据",
                SaveFailureReason.SerializationFailed => "数据序列化失败，无法保存",
                SaveFailureReason.EncryptionFailed => "数据加密失败，无法保存",
                SaveFailureReason.InvalidData => "数据无效，无法保存",
                SaveFailureReason.Cancelled => "保存操作已取消",
                _ => "保存失败，请稍后重试"
            };
        }
    }

    public readonly struct SaveContext
    {
        public SaveSignal Signal { get; }
        public string Key { get; }
        public ISaveData Data { get; }

        public SaveContext(SaveSignal signal, string key, ISaveData data)
        {
            Signal = signal;
            Key = key;
            Data = data;
        }
    }

    public interface ISaveData
    {
        SaveImportance Importance { get; }
    }
}
