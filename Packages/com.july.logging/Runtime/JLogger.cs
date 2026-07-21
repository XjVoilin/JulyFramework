using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace July.Logging
{
    /// <summary>
    /// 日志管理器
    /// 用于替换Unity的Debug，支持通过宏控制日志是否开启
    /// </summary>
    public static class JLogger
    {
        private static ILogReporter _reporter;
        private static LogLevel _minLevel = LogLevel.Debug;
        
        // 启用的日志通道
        private static LogChannel _enabledChannels = July.Logging.LogChannel.All;

        /// <summary>
        /// 设置日志上报器
        /// </summary>
        public static void SetReporter(ILogReporter reporter)
        {
            _reporter = reporter;
        }

        /// <summary>
        /// 设置最小日志级别
        /// </summary>
        public static void SetMinLevel(LogLevel level)
        {
            _minLevel = level;
        }

        /// <summary>
        /// 初始化日志通道配置
        /// </summary>
        public static void InitLogChannels(LogChannel enabledChannels)
        {
            _enabledChannels = enabledChannels;
        }

        /// <summary>
        /// 设置启用的日志通道
        /// </summary>
        public static void SetEnabledChannels(LogChannel channels)
        {
            _enabledChannels = channels;
        }

        /// <summary>
        /// 启用指定通道
        /// </summary>
        public static void EnableChannel(LogChannel channel)
        {
            _enabledChannels |= channel;
        }

        /// <summary>
        /// 禁用指定通道
        /// </summary>
        public static void DisableChannel(LogChannel channel)
        {
            _enabledChannels &= ~channel;
        }

        /// <summary>
        /// 检查指定通道是否启用
        /// </summary>
        public static bool IsChannelEnabled(LogChannel channel)
        {
            return (_enabledChannels & channel) != 0;
        }

        #region 通道级日志方法

        /// <summary>
        /// 输出通道日志（受通道开关控制）
        /// </summary>
        [Conditional("JULYGF_DEBUG")]
        public static void LogChannel(LogChannel channel, string tag, object message)
        {
            if (!IsChannelEnabled(channel)) return;
            InternalLog(LogLevel.Info, $"[{tag}] {message}");
        }

        /// <summary>
        /// 输出通道警告日志（不受通道开关控制）
        /// </summary>
        [Conditional("JULYGF_DEBUG")]
        public static void LogChannelWarning(string tag, object message)
        {
            InternalLog(LogLevel.Warning, $"[{tag}] {message}");
        }

        /// <summary>
        /// 输出通道错误日志（不受通道开关控制）
        /// </summary>
        public static void LogChannelError(string tag, object message)
        {
            InternalLog(LogLevel.Error, $"[{tag}] {message}");
        }

        #endregion

        private static void InternalLog(LogLevel level, object message, Object context = null)
        {
            if (level < _minLevel)
            {
                return;
            }

            var messageStr = message?.ToString() ?? string.Empty;
            
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(messageStr, context);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(messageStr, context);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(messageStr, context);
                    break;
            }

            // 上报错误和致命错误
            if (level >= LogLevel.Error && _reporter != null)
            {
                _reporter.Report(level, messageStr, null);
            }
        }

        private static void InternalLogException(System.Exception exception, Object context = null)
        {
            Debug.LogException(exception, context);
            
            // 上报异常
            if (_reporter != null)
            {
                _reporter.Report(LogLevel.Error, exception.Message, exception);
            }
        }
        #region 普通日志

        /// <summary>
        /// 输出普通日志
        /// </summary>
        /// <param name="message">日志消息</param>
        [Conditional("JULYGF_DEBUG")]
        public static void Log(object message) => InternalLog(LogLevel.Info, message);

        /// <summary>
        /// 输出普通日志（带上下文）
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        [Conditional("JULYGF_DEBUG")]
        public static void Log(object message, Object context) => InternalLog(LogLevel.Info, message, context);
        
        /// <summary>
        /// 输出调试日志
        /// </summary>
        /// <param name="message">日志消息</param>
        [Conditional("JULYGF_DEBUG")]
        public static void LogDebug(object message) => InternalLog(LogLevel.Debug, message);

        /// <summary>
        /// 输出信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        [Conditional("JULYGF_DEBUG")]
        public static void LogInfo(object message) => InternalLog(LogLevel.Info, message);

        #endregion

        #region 警告日志

        /// <summary>
        /// 输出警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        [Conditional("JULYGF_DEBUG")]
        public static void LogWarning(object message) => InternalLog(LogLevel.Warning, message);

        /// <summary>
        /// 输出警告日志（带上下文）
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        [Conditional("JULYGF_DEBUG")]
        public static void LogWarning(object message, Object context) => InternalLog(LogLevel.Warning, message, context);

        #endregion

        #region 错误日志

        /// <summary>
        /// 输出错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogError(object message) => InternalLog(LogLevel.Error, message);

        /// <summary>
        /// 输出错误日志（带上下文）
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="context">上下文对象</param>
        public static void LogError(object message, Object context) => InternalLog(LogLevel.Error, message, context);
        
        /// <summary>
        /// 输出致命错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogFatal(object message) => InternalLog(LogLevel.Fatal, message);

        #endregion

        #region 异常日志

        /// <summary>
        /// 输出异常日志
        /// </summary>
        /// <param name="exception">异常对象</param>
        public static void LogException(System.Exception exception) => InternalLogException(exception);

        /// <summary>
        /// 输出异常日志（带上下文）
        /// </summary>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文对象</param>
        public static void LogException(System.Exception exception, Object context) => InternalLogException(exception, context);

        #endregion

        #region Assert

        /// <summary>
        /// 断言
        /// </summary>
        /// <param name="condition">条件</param>
        /// <param name="message">消息</param>
        [Conditional("JULYGF_DEBUG")]
        public static void Assert(bool condition, object message) => Debug.Assert(condition, message);

        /// <summary>
        /// 断言（带上下文）
        /// </summary>
        /// <param name="condition">条件</param>
        /// <param name="message">消息</param>
        /// <param name="context">上下文对象</param>
        [Conditional("JULYGF_DEBUG")]
        public static void Assert(bool condition, object message, Object context) => Debug.Assert(condition, message, context);

        #endregion
    }
}
