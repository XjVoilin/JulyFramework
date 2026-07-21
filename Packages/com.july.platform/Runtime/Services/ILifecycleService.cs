
namespace July.Platform
{
    public interface ILifecycleService : IPlatformService
    {
        LaunchOptions ColdContext { get; }
        LaunchOptions LatestContext { get; }
    }
}

