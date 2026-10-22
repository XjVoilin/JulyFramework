using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditorInternal;
using UnityEngine;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    internal static class MeshBaselineCache
    {
        internal const string Folder = "Library/July/MeshAuthoring";
        internal static string PathFor(string identity)
        {
            using (var hash = SHA256.Create())
                return Folder + "/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "") + ".bin";
        }
        internal static MeshCachedBaseline Load(string identity)
        {
            string path = PathFor(identity);
            if (!File.Exists(path)) return null;
            var objects = InternalEditorUtility.LoadSerializedFileAndForget(path);
            var entry = objects.OfType<MeshCachedBaseline>().SingleOrDefault();
            if (entry && entry.Version == 1 && entry.Original) return entry;
            foreach (var value in objects) if (value) UnityEngine.Object.DestroyImmediate(value);
            Debug.LogWarning("July Mesh：本机基准缓存失效，将重新建立：" + path);
            return null;
        }
        internal static void Save(string identity, UMesh original, string expectedHash, bool applied, float ratio, bool automatic, int fidelity)
        {
            Directory.CreateDirectory(Folder);
            var entry = ScriptableObject.CreateInstance<MeshCachedBaseline>();
            entry.Original = UnityEngine.Object.Instantiate(original);
            entry.ExpectedHash = expectedHash; entry.Applied = applied; entry.Ratio = ratio; entry.Automatic = automatic; entry.Fidelity = fidelity;
            string path = PathFor(identity), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { entry, entry.Original }, temporary, false);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); entry.Dispose(); }
        }
    }
}
