using System;
using System.Linq;
using UnityEditor;

namespace July.AssetCheck
{
    internal sealed class AssetCheckAssetChanges : AssetPostprocessor
    {
        internal static event Action<string[]> Changed;
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            => Changed?.Invoke(imported.Concat(deleted).Concat(moved).Concat(movedFrom).Distinct().ToArray());
    }
}
