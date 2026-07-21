
namespace July.Platform
{
    public enum AuthorizeType
    {
        Privacy,
        UserInfo,
        Friend,
        UserLocation,
        Community,
        InteractedFriend
    }

    public readonly struct PrivacyAuthorizeResultEvent
    {
        public readonly bool IsSuccess;

        public PrivacyAuthorizeResultEvent(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }

    public readonly struct AuthorizeResultEvent
    {
        public readonly AuthorizeType Type;
        public readonly bool IsSuccess;

        public AuthorizeResultEvent(AuthorizeType type, bool isSuccess)
        {
            Type = type;
            IsSuccess = isSuccess;
        }
    }

    public readonly struct UserInfoResultEvent
    {
        public readonly bool IsSuccess;
        public readonly string NickName;
        public readonly string AvatarUrl;

        public UserInfoResultEvent(bool isSuccess, string nickName = "", string avatarUrl = "")
        {
            IsSuccess = isSuccess;
            NickName = nickName;
            AvatarUrl = avatarUrl;
        }
    }

    public interface IAuthorizeService : IPlatformService
    {
        bool IsNeedPrivacyAuthorization();
        void RequirePrivacyAuthorize();

        bool HasAuthorize(AuthorizeType type);
        void Authorize(AuthorizeType type);

        void RequireUserInfo();
    }
}

