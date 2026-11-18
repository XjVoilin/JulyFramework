using UnityEngine;

namespace July.UI
{
    /// <summary>Shared unscaled clock for UI shaders, including stencil material copies.</summary>
    internal static class UIShaderTime
    {
        private static readonly int UnscaledTimeId = Shader.PropertyToID("_JulyUIUnscaledTime");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Initialize()
        {
            // Enter Play Mode may preserve static event subscriptions without domain reload.
            Shutdown();
            Canvas.willRenderCanvases += PublishTime;
            Application.quitting += Shutdown;
            PublishTime();
        }

        private static void PublishTime()
        {
            Shader.SetGlobalFloat(UnscaledTimeId, UnityEngine.Time.unscaledTime);
        }

        internal static void Shutdown()
        {
            Canvas.willRenderCanvases -= PublishTime;
            Application.quitting -= Shutdown;
        }
    }
}
