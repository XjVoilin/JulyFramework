using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Guide
{
    /// <summary>
    /// Unity main-thread execution, including owner-token cancellation and target callbacks.
    /// Await StopAsync before tearing down resources used by the active Procedure.
    /// </summary>
    public interface IGuideSystem
    {
        bool IsRunning { get; }
        int CurrentGuideId { get; }
        int CurrentStepId { get; }
        string WaitingFor { get; }
        Exception LastFailure { get; }
        Camera WorldCamera { get; }
        UniTask<GuideExitReason?> RunAsync(CancellationToken ct = default);
        UniTask StopAsync();
        UniTask<bool> SkipCurrentGuideAsync();
        void RegisterTarget(IGuideTarget target);
        void UnregisterTarget(IGuideTarget target);
        void RegisterWorldCamera(Camera camera);
        void UnregisterWorldCamera(Camera camera);
    }
}
