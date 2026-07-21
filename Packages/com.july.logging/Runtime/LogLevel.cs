namespace July.Logging
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 跟踪日志（最详细）
        /// </summary>
        Trace = 0,
        
        /// <summary>
        /// 调试日志
        /// </summary>
        Debug = 1,
        
        /// <summary>
        /// 信息日志
        /// </summary>
        Info = 2,
        
        /// <summary>
        /// 警告日志
        /// </summary>
        Warning = 3,
        
        /// <summary>
        /// 错误日志
        /// </summary>
        Error = 4,
        
        /// <summary>
        /// 致命错误日志
        /// </summary>
        Fatal = 5
    }
}

