using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal static class ColliderCache
    {
        [Serializable] sealed class Header { public int version; public string guid; }
        [Serializable] sealed class Record { public int version = 2; public string guid; public Entry[] entries; }
        [Serializable] sealed class Entry
        {
            public string targetId;
            public ColliderGeometryGoal goal;
            public string revision, savedAt, preview;
            public Matrix4x4 worldToItem;
        }
        internal static string PathFor(string assetPath, ColliderAuthoringProfile profile)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) throw new FileNotFoundException("Prefab GUID不可用。", assetPath);
            return Path.GetFullPath(Path.Combine(profile.CacheDirectory, guid + ".json"));
        }
        internal static void Load(ColliderRow row)
        {
            try
            {
                string path = PathFor(row.Path, row.Profile); if (!File.Exists(path)) return;
                string json = AuthoringFile.ReadText(path); var header = JsonUtility.FromJson<Header>(json);
                if (header == null || header.version != 2 || header.guid != row.Guid) throw new InvalidDataException("缓存版本或资源标识无效。");
                row.HasCacheHistory = true;
                var entries = JsonUtility.FromJson<Record>(json).entries;
                if (entries == null) throw new InvalidDataException("缓存缺少目标记录。");
                var parsed = entries.Where(e => e != null && !string.IsNullOrEmpty(e.preview)).Select(entry =>
                {
                    if (string.IsNullOrWhiteSpace(entry.targetId) || string.IsNullOrEmpty(entry.revision) ||
                        !DateTime.TryParseExact(entry.savedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
                        Enumerable.Range(0, 16).Any(k => !float.IsFinite(entry.worldToItem[k])) || Mathf.Abs(entry.worldToItem.determinant) < 1e-10f)
                        throw new InvalidDataException("缓存目标、版本、时间或坐标无效。");
                    var preview = ColliderGeneration.DeserializePreview(entry.preview);
                    if (preview.Goal != entry.goal) throw new InvalidDataException("缓存生成方式与内容不符。");
                    return (entry, preview);
                }).ToArray();
                if (parsed.Select(p => p.entry.targetId + ":" + (int)p.preview.Goal).Distinct().Count() != parsed.Length) throw new InvalidDataException("缓存目标与方式重复。");
                // Publish only after all entries pass boundary validation.
                foreach (var value in parsed)
                {
                    var candidate = row.GetCandidate(value.entry.targetId, value.preview.Goal);
                    candidate.Preview = value.preview; candidate.Revision = value.entry.revision; candidate.WorldToItem = value.entry.worldToItem;
                    candidate.SavedAt = value.entry.savedAt; candidate.Progress = ColliderProgress.Complete; candidate.Fraction = 1;
                    candidate.Stage = $"上次成功预览 · {candidate.Preview.Count} 个碰撞体";
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException)
            { row.CacheError = "缓存不可用，请重新生成：" + error.Message; Debug.LogWarning($"{row.Path} · {row.CacheError}"); }
        }
        internal static void Save(ColliderRow row)
        {
            var record = new Record { guid = row.Guid, entries = row.CandidateStore.Values.Where(c => c.Preview != null).Select(c => new Entry
            { targetId = c.TargetId, goal = c.Goal, revision = c.Revision, savedAt = c.SavedAt, preview = ColliderGeneration.SerializePreview(c.Preview), worldToItem = c.WorldToItem }).ToArray() };
            try
            {
                AuthoringFile.WriteText(PathFor(row.Path,row.Profile),JsonUtility.ToJson(record));
                row.CacheError = null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { row.CacheError = "预览可用，但缓存保存失败：" + error.Message; Debug.LogWarning($"{row.Path} · {row.CacheError}"); }
        }
    }
}
