using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Guide
{
    /// <summary>Capabilities valid for this execution only. There is deliberately no Next/Complete API.</summary>
    public sealed class GuideStepContext
    {
        private readonly GuideSystemBase _system;
        internal readonly int RunId;
        internal readonly CancellationToken RunToken;
        public int GuideId { get; }
        public GuideStepDefinition Step { get; }
        public bool CanSkip { get; }

        internal GuideStepContext(GuideSystemBase system, int runId, GuideDefinition guide,
            GuideStepDefinition step, CancellationToken ct)
        { _system = system; RunId = runId; RunToken = ct; GuideId = guide.Id; Step = step; CanSkip = guide.CanSkip; }

        public UniTask<IGuideTarget> WaitForTargetAsync(int targetId, CancellationToken ct)
            => _system.WaitForTargetAsync(this, targetId, ct);
        public bool IsTargetRegistered(IGuideTarget target) => _system.IsTargetRegistered(target);
        public string ResolveText(string key) => _system.GetText(key);
        public void RequestSkip() => _system.RequestSkip(this);
        public void SetWaitingFor(string description) => _system.SetWaitingFor(this, description);
        /// <summary>Fail an asynchronous presentation/target observation and cancel the unit.</summary>
        public void Fail(Exception error) => _system.Fail(this, error);
    }
}
