using System;
using Cysharp.Threading.Tasks;

namespace July.Guide
{
    /// <summary>
    /// 所有操作均在 Unity 主线程执行，包括停止、跳过和业务回调。
    /// 释放当前 Procedure 使用的资源之前，必须等待 StopAsync 完成。
    /// 取消回调只通知取消，资源清理放在 Procedure 的 finally；不在取消回调中抛出业务异常。
    /// </summary>
    public interface IGuideSystem
    {
        bool IsRunning { get; }
        int CurrentGuideId { get; }
        int CurrentStepId { get; }
        string WaitingFor { get; }
        Exception LastFailure { get; }
        /// <summary>执行就绪的候选计划；运行或清理期间重复触发立即返回 null。未完成计划从头开始，由项目保证重新执行的前提。</summary>
        UniTask<GuideExitReason?> RunAsync();
        /// <summary>停止并等待引导清理；不回滚玩法，不保存内部步骤进度。</summary>
        UniTask StopAsync();
        UniTask<bool> SkipCurrentGuideAsync();
    }
}
