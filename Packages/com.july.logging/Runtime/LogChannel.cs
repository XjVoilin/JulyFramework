using System;

namespace July.Logging
{
    /// <summary>
    /// 日志通道（使用 Flags 枚举，支持多选）
    /// Module 和对应的 Provider 共用同一个通道
    /// </summary>
    [Flags]
    public enum LogChannel
    {
        None = 0,
        
        // 基础模块
        Resource = 1 << 0,
        Time = 1 << 1,
        Pool = 1 << 2,
        Save = 1 << 3,
        Config = 1 << 4,
        Serialize = 1 << 5,
        
        // UI与音频
        UI = 1 << 6,
        Audio = 1 << 7,
        Localization = 1 << 8,
        
        
        // 网络与性能
        Network = 1 << 14,
        Performance = 1 << 15,
        Analytics = 1 << 16,
        HotUpdate = 1 << 17,
        
        // SDK
        Platform = 1 << 18,
        
        // 输入
        Input = 1 << 19,
        
        // 调试
        GM = 1 << 23,
        
        // 其它
        Scene = 1 << 24,
        Fsm = 1 << 25,
        Encryption = 1 << 27,
        
        /// <summary>
        /// 启用所有通道
        /// </summary>
        All = ~0
    }
}

