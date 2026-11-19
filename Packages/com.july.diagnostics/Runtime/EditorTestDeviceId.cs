#if UNITY_EDITOR
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("July.Diagnostics.Editor")]

namespace July.Diagnostics
{
    public static class EditorTestDeviceId
    {
        public static string GetDeviceId()
        {
            var overrideId = EditorTestAccountStore.Override;
            return string.IsNullOrEmpty(overrideId) ? SystemInfo.deviceUniqueIdentifier : overrideId;
        }
    }
}
#endif
