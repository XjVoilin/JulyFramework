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

            ColdContext = new LaunchOptions(true, sceneId, query);
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

                LatestContext = new LaunchOptions(false, hotSceneId, dic);
                this.Publish(new PlatformOnShowEvent(LatestContext));
            };
        }

        public void PostInit()
        {
            _coldLaunchCompleted = true;
        }
    }
}
#endif

