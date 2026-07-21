using July.Arch;
#if JULYGF_WX_MINIGAME
using WeChatWASM;

namespace July.Platform
{
    public class WeChatLifecycleService : ILifecycleService, ICanEvent
    {
        public LaunchOptions ColdContext { get; private set; }
        public LaunchOptions LatestContext { get; private set; }

        private bool _coldLaunchCompleted;

        public void Init()
        {
            var launchOpt = WX.GetLaunchOptionsSync();
            var sceneId = launchOpt.scene.ToString("0");
            var query = launchOpt.query;

            ColdContext = new LaunchOptions(true, sceneId, query);
            LatestContext = ColdContext;

            WX.OnShow(res =>
            {
                if (!_coldLaunchCompleted) return;

                var hotSceneId = res.scene.ToString("0");
                LatestContext = new LaunchOptions(false, hotSceneId, res.query);
                this.Publish(new PlatformOnShowEvent(LatestContext));
            });
        }

        public void PostInit()
        {
            _coldLaunchCompleted = true;
        }
    }
}
#endif

