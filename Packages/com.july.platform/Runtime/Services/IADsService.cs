
namespace July.Platform
{
    public readonly struct RewardedAdResultEvent
    {
        public readonly bool IsShown;
        public readonly bool IsCompleted;

        public RewardedAdResultEvent(bool isShown, bool isCompleted)
        {
            IsShown = isShown;
            IsCompleted = isCompleted;
        }
    }

    public interface IADsService : IPlatformService
    {
        bool HasRewardedAd();
        void PlayRewardedAd();
    }
}

