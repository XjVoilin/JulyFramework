using System.Runtime.InteropServices;

namespace July.Config
{
    /// <summary>
    /// Reads JSON prefetched by the WebGL host. The host contract is the
    /// <c>JulyGetConfigCache</c> JavaScript function.
    /// </summary>
    public static class WebGLConfigCache
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string JulyGetConfigCache();
#endif

        public static string GetCachedJson()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var json = JulyGetConfigCache();
                return string.IsNullOrEmpty(json) ? null : json;
            }
            catch
            {
                return null;
            }
#else
            return null;
#endif
        }
    }
}
