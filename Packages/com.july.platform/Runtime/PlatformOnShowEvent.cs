namespace July.Platform
{
    public readonly struct PlatformOnShowEvent
    {
        public LaunchOptions Context { get; }

        public PlatformOnShowEvent(LaunchOptions context)
        {
            Context = context;
        }
    }
}

