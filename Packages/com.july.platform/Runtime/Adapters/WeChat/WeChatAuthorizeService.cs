using July.Arch;
#if JULYGF_WX_MINIGAME
using System;
using UnityEngine;
using WeChatWASM;

namespace July.Platform
{
    public class WeChatAuthorizeService : IAuthorizeService, INeedGetService, ICanEvent
    {
        private AuthSetting _authSetting;
        private bool _isNeedAuthorization;
        private IDeviceService _deviceService;

        public Func<Type, object> ServiceGetter { get; set; }

        private const string ScopeUserFuzzyLocation = "scope.userFuzzyLocation";
        private const string ScopeUserInfo = "scope.userInfo";
        private const string ScopeWxFriendInteraction = "scope.WxFriendInteraction";
        private const string ScopeGameClubData = "scope.gameClubData";
        private const string ScopeInteractedUserInfo = "scope.interactedUserInfo";

        public void Init() { }

        public void DeferredInit()
        {
            RefreshUserSetting();
            RefreshPrivacy();
        }

        public void PostInit()
        {
            _deviceService = this.GetService<IDeviceService>();
        }

        private void RefreshUserSetting()
        {
            var info = new GetSettingOption
            {
                fail = _ => { },
                success = res => { _authSetting = res.authSetting; }
            };
            WX.GetSetting(info);
        }

        private void RefreshPrivacy()
        {
            if (_isNeedAuthorization) return;

            var opt = new GetPrivacySettingOption
            {
                fail = _ => { _isNeedAuthorization = false; },
                success = res => { _isNeedAuthorization = res.needAuthorization; }
            };
            WX.GetPrivacySetting(opt);
        }

        public bool IsNeedPrivacyAuthorization() => _isNeedAuthorization;

        public void RequirePrivacyAuthorize()
        {
            var options = new RequirePrivacyAuthorizeOption
            {
                success = _ => this.Publish(new PrivacyAuthorizeResultEvent(true)),
                fail = _ => this.Publish(new PrivacyAuthorizeResultEvent(false)),
            };
            WX.RequirePrivacyAuthorize(options);
        }

        public bool HasAuthorize(AuthorizeType type)
        {
            return type switch
            {
                AuthorizeType.Privacy => !_isNeedAuthorization,
                AuthorizeType.UserInfo => HasScope(ScopeUserInfo),
                AuthorizeType.Friend => HasScope(ScopeWxFriendInteraction),
                AuthorizeType.UserLocation => _deviceService?.IsPc() == true || HasScope(ScopeUserFuzzyLocation),
                AuthorizeType.Community => HasScope(ScopeGameClubData),
                AuthorizeType.InteractedFriend => HasScope(ScopeInteractedUserInfo),
                _ => false
            };
        }

        public void Authorize(AuthorizeType type)
        {
            var scope = type switch
            {
                AuthorizeType.Friend => ScopeWxFriendInteraction,
                AuthorizeType.Community => ScopeGameClubData,
                AuthorizeType.UserLocation => ScopeUserFuzzyLocation,
                AuthorizeType.UserInfo => ScopeUserInfo,
                AuthorizeType.InteractedFriend => ScopeInteractedUserInfo,
                _ => null
            };

            if (scope == null)
            {
                this.Publish(new AuthorizeResultEvent(type, false));
                return;
            }

            if (type == AuthorizeType.UserInfo)
            {
                AuthorizeUserInfoViaButton(success =>
                    this.Publish(new AuthorizeResultEvent(type, success)));
                return;
            }

            var option = new AuthorizeOption
            {
                scope = scope,
                success = _ =>
                {
                    SetScope(scope);
                    this.Publish(new AuthorizeResultEvent(type, true));
                },
                fail = _ => this.Publish(new AuthorizeResultEvent(type, false)),
            };
            WX.Authorize(option);
        }

        public void RequireUserInfo()
        {
            if (HasScope(ScopeUserInfo))
            {
                var opt = new GetUserInfoOption
                {
                    withCredentials = false,
                    lang = "zh_CN",
                    success = data =>
                        this.Publish(new UserInfoResultEvent(true, data.userInfo.nickName, data.userInfo.avatarUrl)),
                    fail = _ =>
                        this.Publish(new UserInfoResultEvent(false)),
                };
                WX.GetUserInfo(opt);
            }
            else
            {
                AuthorizeUserInfoViaButton();
            }
        }

        private void AuthorizeUserInfoViaButton(Action<bool> authorizeCallback = null)
        {
            var btn = WX.CreateUserInfoButton(0, 0, Screen.width, Screen.height, "zh_CN", false);
            btn.OnTap(response =>
            {
                if (response.errCode == 0)
                {
                    SetScope(ScopeUserInfo);
                    authorizeCallback?.Invoke(true);
                    this.Publish(new UserInfoResultEvent(true,
                        response.userInfo.nickName, response.userInfo.avatarUrl));
                }
                else
                {
                    authorizeCallback?.Invoke(false);
                    this.Publish(new UserInfoResultEvent(false));
                }
                btn.Destroy();
            });
        }

        private bool HasScope(string scope)
        {
            if (_authSetting == null) return false;
            return _authSetting.ContainsKey(scope) && _authSetting[scope];
        }

        private void SetScope(string scope)
        {
            if (_authSetting != null) _authSetting[scope] = true;
        }
    }
}
#endif

