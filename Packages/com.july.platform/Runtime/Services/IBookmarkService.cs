
namespace July.Platform
{
    public interface IBookmarkService : IPlatformService
    {
        void ShowFavoriteGuide();
        void NavigateToSidebar();
        bool IsSidebarSupported { get; }
    }
}

