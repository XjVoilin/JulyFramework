using July.Arch;
namespace July.Platform
{
    public class DefaultADsService : IADsService, ICanEvent
    {
        public void DeferredInit() { }

        public bool HasRewardedAd() => true;

        public void PlayRewardedAd()
        {
            this.Publish(new RewardedAdResultEvent(true, true));
        }
    }
}

