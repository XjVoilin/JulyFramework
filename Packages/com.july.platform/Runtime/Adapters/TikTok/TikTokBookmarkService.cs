#if JULYGF_DY_MINIGAME
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace July.Platform
{
    public class TikTokBookmarkService : IBookmarkService
    {
        private bool _isSidebarSupported;

        public bool IsSidebarSupported => _isSidebarSupported;

        public void Init()
        {
            TT.CheckScene(TTSideBar.SceneEnum.SideBar,
                supported => { _isSidebarSupported = supported; },
                () => { },
                (_, _) => { });
        }

        public void ShowFavoriteGuide()
        {
            TT.ShowRevisitGuide(_ => { });
        }

        public void NavigateToSidebar()
        {
            var jsonData = new JsonData { ["scene"] = "sidebar" };
            TT.NavigateToScene(jsonData,
                () => { },
                () => { },
                (_, _) => { });
        }
    }
}
#endif

