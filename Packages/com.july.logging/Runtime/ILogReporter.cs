namespace July.Logging
{
    /// <summary>
    /// 日志上报接口
    /// 用于将日志上报到服务器（手游需要）
    /// </summary>
    public interface ILogReporter
    {
        /// <summary>
        /// 上报日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象（如果有）</param>
        void Report(LogLevel level, string message, System.Exception exception = null);
    }
}

