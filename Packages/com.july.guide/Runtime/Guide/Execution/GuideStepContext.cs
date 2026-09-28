using System.Threading;

namespace July.Guide
{
    /// <summary>仅在本次执行中有效的能力入口，不提供 Next/Complete 接口。</summary>
    public sealed class GuideStepContext
    {
        private readonly GuideSystemBase _system;
        internal readonly CancellationToken RunToken;
        public int GuideId { get; }
        public GuideStep Step { get; }
        public bool CanSkip { get; }

        internal GuideStepContext(GuideSystemBase system, GuidePlan guide,
            GuideStep step, CancellationToken ct)
        {
            _system = system;
            RunToken = ct;
            GuideId = guide.Id;
            Step = step;
            CanSkip = guide.CanSkip;
        }

        public void RequestSkip() => _system.RequestSkip(this);
        public void SetWaitingFor(string description) => _system.SetWaitingFor(this, description);

    }
}