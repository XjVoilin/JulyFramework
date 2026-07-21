using July.Arch;
#if JULYGF_DY_MINIGAME
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;

namespace July.Platform
{
    public readonly struct FeedSubscribeResultEvent
    {
        public readonly bool IsSuccess;
        public FeedSubscribeResultEvent(bool isSuccess) => IsSuccess = isSuccess;
    }

    public class TikTokSubscribeService : ISubscribeService, ICanEvent
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
                    Debug.Log($"[TikTokSubscribe] 鎺ㄨ崘娴佽闃? success={isSuccess}");
                    this.Publish(new FeedSubscribeResultEvent(isSuccess));
                },
                (errNo, errMsg) =>
                {
                    _isFeedSubscribing = false;
                    Debug.LogWarning($"[TikTokSubscribe] 鎺ㄨ崘娴佽闃呭け璐? errNo={errNo}, errMsg={errMsg}");
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
                    Debug.Log($"[TikTokSubscribe] 鑾峰緱鎺ㄨ崘娴佽闃呯姸鎬? subscribed={subscribed}");
                    this.Publish(new FeedSubscribeResultEvent(subscribed));
                },
                (errNo, errMsg) =>
                {
                    Debug.LogWarning($"[TikTokSubscribe] 鑾峰緱鎺ㄨ崘娴佽闃呯姸鎬佸け璐? errNo={errNo}, errMsg={errMsg}");
                },
                null
            );
        }
    }
}
#endif

