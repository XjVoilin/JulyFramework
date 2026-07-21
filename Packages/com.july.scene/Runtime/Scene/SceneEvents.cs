using UnityEngine.SceneManagement;

namespace July.Scene
{
    public class SceneLoadStartEvent
    {
        public string SceneName { get; set; }
        public LoadSceneMode LoadMode { get; set; }
    }

    public class SceneLoadCompleteEvent
    {
        public string SceneName { get; set; }
        public UnityEngine.SceneManagement.Scene Scene { get; set; }
        public LoadSceneMode LoadMode { get; set; }
    }

    public class SceneUnloadStartEvent
    {
        public string SceneName { get; set; }
    }

    public class SceneUnloadCompleteEvent
    {
        public string SceneName { get; set; }
        public bool Success { get; set; }
    }

    public class SceneSwitchStartEvent
    {
        public string FromSceneName { get; set; }
        public string ToSceneName { get; set; }
    }

    public class SceneSwitchCompleteEvent
    {
        public string FromSceneName { get; set; }
        public string ToSceneName { get; set; }
        public UnityEngine.SceneManagement.Scene Scene { get; set; }
    }
}
