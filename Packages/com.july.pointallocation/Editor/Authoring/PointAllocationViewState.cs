using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.PointAllocation.Editor
{
    [Serializable]
    internal sealed class PointAllocationViewState
    {
        public Vector3 position;
        public Vector3 scale = Vector3.one;
        internal static string PathFor(TextAsset json)
        {
            string assetPath = AssetDatabase.GetAssetPath(json);
            return string.IsNullOrEmpty(assetPath) ? null : "Library/July/PointAllocation/" + AssetDatabase.AssetPathToGUID(assetPath) + ".json";
        }
        internal static PointAllocationViewState Load(TextAsset json)
        {
            string path = PathFor(json);
            if (path == null || !File.Exists(path)) return new PointAllocationViewState();
            return JsonUtility.FromJson<PointAllocationViewState>(File.ReadAllText(path))
                ?? throw new InvalidDataException("加点图视角状态无效：" + path);
        }
        internal void Save(TextAsset json)
        {
            string path = PathFor(json);
            if (path == null) return; // Unsaved documents only hold their view in memory.
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(this));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
