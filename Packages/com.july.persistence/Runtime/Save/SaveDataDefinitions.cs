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

    /// <summary>
    /// 存档数据的重要程度，同时决定自动保存的时效。
    /// Critical 在 Store 标脏时立即进入写入队列；其余级别等待对应的保存信号。
    /// </summary>
    public enum SaveImportance
    {
        /// <summary>标脏后立即排队保存，失败后仍保持脏状态并参与后续重试。</summary>
        Critical = 0,

        /// <summary>响应 Medium、High 或 Immediate 保存信号。</summary>
        Important = 1,

        /// <summary>响应 High 或 Immediate 保存信号。</summary>
        Normal = 2,

        /// <summary>仅响应 Immediate 保存信号。</summary>
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

}
