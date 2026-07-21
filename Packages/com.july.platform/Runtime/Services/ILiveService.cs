
namespace July.Platform
{
    public readonly struct InstantPlayChangedEvent
    {
        public readonly bool IsActive;
        public InstantPlayChangedEvent(bool isActive) => IsActive = isActive;
    }

    public interface ILiveService : IPlatformService
    {
        bool IsInLive { get; }
        bool IsAnchor { get; }
        bool IsInstantPlay { get; }
        string GameProgress { get; }
        void ReportGameProgress(string progress);
    }
}

