using July.Arch;
#if JULYGF_DY_MINIGAME
using TTSDK;

namespace July.Platform
{
    public class TikTokAuthorizeService : IAuthorizeService, ICanEvent
    {
        private AuthSetting _authSetting;

        public void Init()
        {
            RefreshUserSetting();
        }

        private void RefreshUserSetting()
        {
            TT.GetSetting(setting =>
            {
                _authSetting = setting;
            }, _ => { });
        }

        public bool IsNeedPrivacyAuthorization() => false;

        public void RequirePrivacyAuthorize()
        {
            this.Publish(new PrivacyAuthorizeResultEvent(true));
        }

        public bool HasAuthorize(AuthorizeType type)
        {
            return type switch
            {
                AuthorizeType.Privacy => true,
                AuthorizeType.Friend => true,
                AuthorizeType.UserInfo => _authSetting?.UserInfo ?? false,
                AuthorizeType.UserLocation => true,
                AuthorizeType.Community => false,
                _ => false
            };
        }

        public void Authorize(AuthorizeType type)
        {
            if (type == AuthorizeType.Friend)
            {
                this.Publish(new AuthorizeResultEvent(type, true));
                return;
            }

            var scope = type switch
            {
                AuthorizeType.UserInfo => AuthorizeScope.UserInfo,
                AuthorizeType.UserLocation => AuthorizeScope.UserLocation,
                AuthorizeType.Community => AuthorizeScope.GameClubData,
                _ => null
            };

            if (scope == null)
            {
                this.Publish(new AuthorizeResultEvent(type, false));
                return;
            }

            TT.Authorize(scope,
                (_, _) =>
                {
                    if (_authSetting != null) _authSetting[scope] = true;
                    this.Publish(new AuthorizeResultEvent(type, true));
                },
                (_, _) => this.Publish(new AuthorizeResultEvent(type, false)));
        }

        public void RequireUserInfo()
        {
            TT.GetUserInfo(
                (ref TTUserInfo userInfo) =>
                {
                    if (_authSetting != null) _authSetting[AuthorizeScope.UserInfo] = true;
                    this.Publish(new UserInfoResultEvent(true, userInfo.nickName, userInfo.avatarUrl));
                },
                _ => this.Publish(new UserInfoResultEvent(false)));
        }
    }
}
#endif

