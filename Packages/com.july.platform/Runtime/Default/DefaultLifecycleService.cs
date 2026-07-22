namespace July.Platform
{
    public class DefaultLifecycleService : ILifecycleService
    {
        public LaunchOptions ColdContext { get; }
        public LaunchOptions LatestContext { get; }

        public void Restart()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        public void Exit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }
    }
}
