using System;
using System.IO;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    internal static class MatCapAuthoringFiles
    {
        internal const string DirectoryPath = "July/MatCapAuthoring";

        internal static void Write(string path, object value, bool replace)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(value, true) + "\n");
                if (replace && File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
