using July.Arch;
#if JULYGF_DY_MINIGAME
using System.Collections.Generic;
using TTSDK;

namespace July.Platform
{
    public class TikTokLifecycleService : ILifecycleService, ICanEvent
    {
        public LaunchOptions ColdContext { get; private set; }
        public LaunchOptions LatestContext { get; private set; }

        private bool _coldLaunchCompleted;

        public void Init()
        {
            var launchOpt = TT.GetLaunchOptionsSync();
            var sceneId = launchOpt.Scene ?? "";
            var query = launchOpt.Query;

            ColdContext = CreateLaunchOptions(true, sceneId, query);
            LatestContext = ColdContext;

            TT.GetAppLifeCycle().OnShow += res =>
            {
                if (!_coldLaunchCompleted) return;

                var dic = new Dictionary<string, string>();
                string hotSceneId = "";
                foreach (var kv in res)
                {
                    if (kv.Value is not string value) continue;
                    if (kv.Key == "scene")
                    {
                        hotSceneId = value;
                        continue;
                    }
                    dic[kv.Key] = value;
                }

                LatestContext = CreateLaunchOptions(false, hotSceneId, dic);
                this.Publish(new PlatformOnShowEvent(LatestContext));
            };
        }

        public void PostInit()
        {
            _coldLaunchCompleted = true;
        }

        public void Restart() => TT.RestartMiniProgramSync();

        public void Exit() => TT.ExitMiniProgram(false);

        private static LaunchOptions CreateLaunchOptions(bool isColdStart,
            string sceneId, IReadOnlyDictionary<string, string> query)
        {
            if (IsLiveScene(sceneId))
                return new LaunchOptions(isColdStart, sceneId, query,
                    source: LaunchSource.Live);

            if (query != null &&
                query.TryGetValue("feed_game_channel", out var rawChannel) &&
                int.TryParse(rawChannel, out var channel) &&
                query.TryGetValue("feed_game_content_id", out var contentId))
            {
                var normalizedChannel = channel switch
                {
                    1 => FeedLaunchChannel.Revisit,
                    2 => FeedLaunchChannel.Acquisition,
                    _ => FeedLaunchChannel.None,
                };
                if (normalizedChannel != FeedLaunchChannel.None)
                    return new LaunchOptions(isColdStart, sceneId, query,
                        source: LaunchSource.Feed,
                        feedChannel: normalizedChannel,
                        contentId: contentId);
            }

            return new LaunchOptions(isColdStart, sceneId, query);
        }

        private static bool IsLiveScene(string sceneId) =>
            !string.IsNullOrEmpty(sceneId) &&
            (sceneId.EndsWith("3009") || sceneId.EndsWith("3010") ||
             sceneId.EndsWith("9003"));
    }
}
#endif
