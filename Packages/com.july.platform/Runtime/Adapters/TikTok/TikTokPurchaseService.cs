using July.Arch;
#if JULYGF_DY_MINIGAME
using System;
using System.Collections.Generic;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace July.Platform
{
    public class TikTokPurchaseService : IPurchaseService, ICanEvent
    {
        public void Purchase(int orderAmount)
        {
            TT.CheckSession(
                () =>
                {
                    var platform = TT.GetSystemInfo().platform;
                    if (platform == "ios")
                        PurchaseIOS(orderAmount);
                    else if (platform == "android")
                        PurchaseAndroid(orderAmount);
                    els…4952 tokens truncated…uthorizeType.Community => ScopeGameClubData,
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

