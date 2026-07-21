namespace July.Platform
{
    public class DefaultLifecycleService : ILifecycleService
    {
        public LaunchOptions ColdContext { get; }
        public LaunchOptions LatestContext { get; }
    }
}

