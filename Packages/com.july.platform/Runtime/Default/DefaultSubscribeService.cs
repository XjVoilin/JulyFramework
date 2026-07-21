using July.Arch;
namespace July.Platform
{
    public class DefaultSubscribeService : ISubscribeService, ICanEvent
    {
        public void SubscribeOnce(string[] templateIds)
        {
            this.Publish(new SubscribeResultEvent(true, false));
        }

        public void SubscribeLongTerm(string[] templateIds)
        {
            this.Publish(new SubscribeResultEvent(true, true));
        }
    }
}

