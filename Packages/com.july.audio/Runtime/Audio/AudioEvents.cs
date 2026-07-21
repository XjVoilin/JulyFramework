namespace July.Audio
{
    public readonly struct AudioPlayCompleteEvent
    {
        public string AudioName { get; }
        public bool WasStopped { get; }

        public AudioPlayCompleteEvent(string audioName, bool wasStopped)
        {
            AudioName = audioName;
            WasStopped = wasStopped;
        }
    }
}
