using System.Runtime.InteropServices;

namespace July.Config
{
    /// <summary>
    /// 读取 WebGL 宿主预取的 JSON，宿主通过
    /// <c>JulyGetConfigCache</c> JavaScript 函数提供缓存。
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
