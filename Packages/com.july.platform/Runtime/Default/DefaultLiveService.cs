namespace July.Platform
{
    public class DefaultLiveService : ILiveService
    {
        public bool IsInLive { get; set; }
        public bool IsAnchor { get; set; }
        public bool IsInstantPlay { get; set; }
        public string GameProgress { get; set; }

        public void PostInit()
        {
        }

        public void ReportGameProgress(string progress)
        {
        }
    }
}

