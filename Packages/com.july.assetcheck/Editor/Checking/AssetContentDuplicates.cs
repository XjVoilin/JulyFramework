using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace July.AssetCheck
{
    // 集合级只读检查；调用方决定输入集合，不自行扩展目录或应用忽略。
    internal static class AssetContentDuplicates
    {
        internal static IEnumerable<bool> Check(IEnumerable<string> paths, Action<string[]> duplicate, Action<string, string> unavailable)
        {
            var sizes = new Dictionary<long, List<string>>();
            foreach (var path in paths.Distinct(StringComparer.Ordinal))
            {
                long length = 0; string failure = null;
                try { length = new FileInfo(path).Length; }
                catch (IOException e) { failure = e.Message; }
                catch (UnauthorizedAccessException e) { failure = e.Message; }
                if (failure != null) unavailable(path, "内容未能读取：" + failure);
                else
                {
                    if (!sizes.TryGetValue(length, out var group)) sizes.Add(length, group = new List<string>());
                    group.Add(path);
                }
                yield return true;
            }
            var hashes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var buffer = new byte[65536];
            foreach (var path in sizes.Values.Where(group => group.Count > 1).SelectMany(group => group))
            {
                FileStream stream = null; string failure = null;
                DateTime stamp = default; long length = 0;
                try { stream = File.OpenRead(path); var before = new FileInfo(path); stamp = before.LastWriteTimeUtc; length = stream.Length; }
                catch (IOException e) { failure = e.Message; }
                catch (UnauthorizedAccessException e) { failure = e.Message; }
                if (failure != null) { stream?.Dispose(); unavailable(path, "内容未能读取：" + failure); continue; }
                using (stream)
                using (var hash = SHA256.Create())
                {
                    while (failure == null)
                    {
                        var count = 0;
                        try { count = stream.Read(buffer, 0, buffer.Length); }
                        catch (IOException e) { failure = e.Message; }
                        if (count == 0 || failure != null) break;
                        hash.TransformBlock(buffer, 0, count, buffer, 0);
                        yield return true;
                    }
                    if (failure == null)
                    {
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        var after = new FileInfo(path);
                        if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != stamp) failure = "文件在读取期间发生变化";
                        else
                        {
                            var key = length + ":" + BitConverter.ToString(hash.Hash);
                            if (!hashes.TryGetValue(key, out var group)) hashes.Add(key, group = new List<string>());
                            group.Add(path);
                        }
                    }
                    if (failure != null) unavailable(path, "内容未能验证：" + failure);
                }
            }
            foreach (var group in hashes.Values.Where(group => group.Count > 1)) duplicate(group.ToArray());
        }
    }
}
