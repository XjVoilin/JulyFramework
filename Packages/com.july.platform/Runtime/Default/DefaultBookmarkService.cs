namespace July.Platform
{
    public class DefaultBookmarkService : IBookmarkService
    {
        public void ShowFavoriteGuide() { }
        public void NavigateToSidebar() { }
        public bool IsSidebarSupported => false;
    }
}

