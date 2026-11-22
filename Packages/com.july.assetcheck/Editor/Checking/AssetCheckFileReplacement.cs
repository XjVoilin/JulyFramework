using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace July.AssetCheck
{
    internal static class AssetCheckFileReplacement
    {
        internal static void Commit(string temporary, string path, bool allowCreate = false, Action<int> wait = null)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int unableToRemoveReplaced = unchecked((int)0x80070497);

            for (var delay = 16; ; delay *= 2)
            {
                try
                {
                    if (allowCreate && !File.Exists(path)) File.Move(temporary, path);
                    else File.Replace(temporary, path, null);
                    return;
                }
                // Windows 读句柄可能暂时禁止替换。两种错误都保留原文件和临时文件，可安全重试。
                catch (IOException exception) when (delay <= 64 && Application.platform == RuntimePlatform.WindowsEditor &&
                    (exception.HResult == sharingViolation || exception.HResult == unableToRemoveReplaced))
                {
                    (wait ?? Thread.Sleep)(delay);
                }
            }
        }
    }
}
