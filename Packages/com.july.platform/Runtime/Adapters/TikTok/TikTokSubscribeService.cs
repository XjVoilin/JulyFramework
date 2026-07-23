using July.Arch;
#if JULYGF_DY_MINIGAME
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;

namespace July.Platform
{
    public class TikTokSubscribeService : ITikTokSubscribeService, ICanEvent
    {
        private bool _isFeedSubscribing;

        public void SubscribeOnce(string[] templateIds)
        {
            this.Publish(new SubscribeResultEvent(true, false));
        }

        public void SubscribeLongTerm(string[] templateIds)
        {
            this.Publish(new SubscribeResultEvent(true, true));
        }

        public void FollowTiktokFeedSubscribe()
        {
            if (_isFeedSubscribing)
                return;

            _isFeedSubscribing = true;
            var param = new JsonData
            {
                ["type"] = "play",
                ["allScene"] = true
            };
            TT.RequestFeedSubscribe(
                param,
                res =>
                {
                    _isFeedSubscribing = false;
                    var isSuccess = res.ContainsKey("success")
                                   && res["success"].IsBoolean
                                   && (bool)res["success"];
                    Debug.Log($"[TikTokSubscribe] 推荐流订阅: success={isSuccess}");
                    this.Publish(new FeedSubscribeResultEvent(isSuccess));
                },
                (errNo, errMsg) =>
                {
                    _isFeedSubscribing = false;
                    Debug.LogWarning($"[TikTokSubscribe] 推荐流订阅失败: errNo={errNo}, errMsg={errMsg}");
                    this.Publish(new FeedSubscribeResultEvent(false));
                },
                null
            );
        }
        
        public void CheckFeedSubscribeStatus()
        {
            var param = new JsonData
            {
                ["type"] = "play",
                ["allScene"] = true
            };
            TT.CheckFeedSubscribeStatus(
                param,
                res =>
                {
                    var subscribed = res.ContainsKey("success")
                                    && res["success"].IsBoolean
                                    && (bool)res["success"];
                    Debug.Log($"[TikTokSubscribe] 获得推荐流订阅状态: subscribed={subscribed}");
                    this.Publish(new FeedSubscribeResultEvent(subscribed));
                },
                (errNo, errMsg) =>
                {
                    Debug.LogWarning($"[TikTokSubscribe] 获得推荐流订阅状态失败: errNo={errNo}, errMsg={errMsg}");
                },
                null
            );
        }
    }
}
#endif
