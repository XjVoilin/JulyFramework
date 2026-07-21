using July.Arch;
namespace July.Platform
{
    public class DefaultAuthorizeService : IAuthorizeService, ICanEvent
    {
        public bool IsNeedPrivacyAuthorization() => false;

        public void RequirePrivacyAuthorize()
        {
            this.Publish(new PrivacyAuthorizeResultEvent(true));
        }

        public bool HasAuthorize(AuthorizeType type) => true;

        public void Authorize(AuthorizeType type)
        {
            this.Publish(new AuthorizeResultEvent(type, true));
        }

        public void RequireUserInfo()
        {
            this.Publish(new UserInfoResultEvent(true, "Editor", ""));
        }
    }
}

