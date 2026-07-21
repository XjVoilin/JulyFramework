
namespace July.Platform
{
    public readonly struct SubscribeResultEvent
    {
        public readonly bool IsSuccess;
        public readonly bool IsLongTerm;

        public SubscribeResultEvent(bool isSuccess, bool isLongTerm)
        {
            IsSuccess = isSuccess;
            IsLongTerm = isLongTerm;
        }
    }

    public interface ISubscribeService : IPlatformService
    {
        void SubscribeOnce(string[] templateIds);
        void SubscribeLongTerm(string[] templateIds);
    }

    public readonly struct FeedSubscribeResultEvent
    {
        public readonly bool IsSuccess;

        public FeedSubscribeResultEvent(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }

    public interface ITikTokSubscribeService : ISubscribeService
    {
        void FollowTiktokFeedSubscribe();
        void CheckFeedSubscribeStatus();
    }
}
