using System;
using System.Linq;
using UnityEditor;

namespace July.AssetCheck
{
    internal sealed class AssetCheckAssetChanges : AssetPostprocessor
    {
        internal static event Action<string[]> Changed;
        internal static int Revision { get; private set; }
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            Revision++;
            Changed?.Invoke(imported.Concat(deleted).Concat(moved).Concat(movedFrom).Distinct().ToArray());
        }
    }
}
