using July.Arch;
#if JULYGF_DY_MINIGAME
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;

namespace July.Platform
{
    public class TikTokFeedService : ITikTokFeedService, ICanEvent
    {
        private const string Tag = "[TikTokFeed]";

        public void Init() { }

        public void PostInit()
        {
            TT.OnFeedStatusChange(result =>
            {
                var isEnter = result.Type == FeedStatusEnum.FeedEnter;
                Debug.Log($"{Tag} FeedStatusChange: isEnter={isEnter}");
                this.Publish(new FeedStatusChangedEvent(isEnter));
            });
        }

        public void ReportScene(int sceneId)
        {
            var param = new JsonData
            {
                ["sceneId"] = sceneId,
                ["costTime"] = 0,
            };
            TT.ReportScene(
                param,
                _ => Debug.Log($"{Tag} ReportScene {sceneId} 成功"),
                (code, msg) => Debug.LogError($"{Tag} ReportScene {sceneId} 失败: code={code}, msg={msg}"));
        }
    }
}
#endif

