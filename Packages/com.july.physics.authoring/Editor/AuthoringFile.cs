using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace July.Physics.Authoring
{
    // One atomic-file contract for author records and disposable caches. Callers
    // retain ownership of reporting, rollback and cache-miss behavior.
    internal static class AuthoringFile
    {
        internal static FileStream OpenRead(string path) =>
            new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read | FileShare.Delete);

        internal static string ReadText(string path)
        { using var reader = new StreamReader(OpenRead(path),Encoding.UTF8,true); return reader.ReadToEnd(); }

        internal static void WriteText(string path, string text) =>
            WriteAtomic(path,temporary => File.WriteAllText(temporary,text,new UTF8Encoding(false)));

        internal static void WriteAtomic(string path, Action<string> writeTemporary)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                writeTemporary(temporary);
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
                        return;
                    }
                    catch (IOException error) when (attempt < 2 && TransientReplaceConflict(error))
                    { Thread.Sleep(20); }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static bool TransientReplaceConflict(IOException error)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
            int code = error.HResult & 0xffff;
            // Sharing/lock violations, including the observed ReplaceFile failure
            // ERROR_UNABLE_TO_REMOVE_REPLACED. Permissions and other IO errors fail.
            return code == 32 || code == 33 || code == 1175;
        }
    }
}
