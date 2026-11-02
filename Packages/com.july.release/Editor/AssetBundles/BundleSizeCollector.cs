using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Injector;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.WriteTypes;

namespace July.Release.Editor
{
    internal sealed class BundleSizeCollector : IBuildTask
    {
        public int Version => 1;
        [InjectContext(ContextUsage.In)] IBundleWriteData _writeData;
        [InjectContext(ContextUsage.In)] IBundleBuildResults _results;
        internal BundleSizeRecord[] Bundles { get; private set; }

        internal sealed class WrittenFile { internal string Alias, Bundle, Path, Scene; internal long Bytes; internal bool Serialized; }
        internal sealed class WrittenObject
        {
            internal SerializedBundleData.ObjectData Data;
            internal string Bundle, Guid, Path;
            internal string Key => AssetBundleWrittenSize.Identity(Guid, Path);
        }
        public ReturnCode Run()
        {
            var files = new Dictionary<string, WrittenFile>(StringComparer.Ordinal);
            var sources = new Dictionary<(string, long), (string Guid, string Path)>();
            foreach (var op in _writeData.WriteOperations)
            {
                var bundle = _writeData.FileToBundle[op.Command.internalName];
                string scene = op is SceneBundleWriteOperation first ? first.Scene : op is SceneDataWriteOperation later ? later.Scene : null;
                foreach (var obj in op.Command.serializeObjects)
                {
                    var guid = obj.serializationObject.guid.ToString();
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (IsSource(path)) sources.Add((op.Command.fileName, obj.serializationIndex), (guid, path));
                }
                foreach (var resource in _results.WriteResults[op.Command.internalName].resourceFiles)
                {
                    var alias = SerializedBundleData.Alias(resource.fileAlias);
                    files.Add(alias, new WrittenFile { Alias = alias, Bundle = bundle, Path = resource.fileName, Bytes = new FileInfo(resource.fileName).Length, Serialized = resource.serializedFile,
                        Scene = resource.serializedFile && alias != op.Command.fileName ? scene : null });
                }
            }
            var objects = new List<WrittenObject>();
            using (var reader = new SerializedBundleData())
                foreach (var file in files.Values.Where(f => f.Serialized))
                    foreach (var data in reader.Read(file.Path, file.Alias))
                    {
                        var source = sources.TryGetValue((file.Alias, data.Id), out var mapped) ? mapped :
                            string.IsNullOrEmpty(file.Scene) ? (null, null) : (AssetDatabase.AssetPathToGUID(file.Scene), file.Scene);
                        objects.Add(new WrittenObject { Data = data, Bundle = file.Bundle, Guid = source.Item1, Path = source.Item2 });
                    }
            Bundles = Attribute(files.Values.ToArray(), objects.ToArray());
            return ReturnCode.Success;
        }
        static bool IsSource(string path) => path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal);

        // 来源映射和物理数据是唯一输入；对象自身、共享区间和结构开销都在此汇总。
        internal static BundleSizeRecord[] Attribute(WrittenFile[] files, WrittenObject[] objects)
        {
            var byObject = objects.ToDictionary(o => (o.Data.File, o.Data.Id));
            var atlasOwners = new Dictionary<WrittenObject, HashSet<WrittenObject>>();
            foreach (var atlas in objects.Where(o => o.Data.AtlasTextures.Count > 0 && o.Path != null))
                foreach (var reference in atlas.Data.AtlasTextures)
                {
                    if (!byObject.TryGetValue((reference.File, reference.Id), out var texture)) { MarkPartial(atlas, "生成纹理引用未在本次构建找到，未统计其大小"); continue; }
                    if (texture.Path != null) continue; // 有自身源资源的纹理属于独立依赖。
                    if (!atlasOwners.TryGetValue(texture, out var owners)) atlasOwners.Add(texture, owners = new HashSet<WrittenObject>());
                    owners.Add(atlas);
                }
            foreach (var pair in atlasOwners)
            {
                var owners = pair.Value.GroupBy(o => o.Key).Select(g => g.First()).ToArray();
                if (owners.Length == 1 && pair.Key.Bundle == owners[0].Bundle) { pair.Key.Guid = owners[0].Guid; pair.Key.Path = owners[0].Path; }
                else if (owners.Length == 1) MarkPartial(owners[0], "生成纹理位于依赖包，其大小在实际包的生成数据中单列");
                else foreach (var owner in owners) MarkPartial(owner, "多个图集共享生成纹理，其大小在生成数据中单列");
            }
            var output = new List<BundleSizeRecord>();
            var fileSizes = files.ToDictionary(f => f.Alias, StringComparer.Ordinal);
            foreach (var group in files.GroupBy(f => f.Bundle).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var bundleObjects = objects.Where(o => o.Bundle == group.Key).ToArray();
                var resources = new Dictionary<string, ResourceSizeRecord>(StringComparer.Ordinal);
                var generated = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var obj in bundleObjects) AddBytes(obj, 0, resources, generated);
                var ranges = bundleObjects.SelectMany(o => o.Data.Streams.Concat(new[] {
                    new SerializedBundleData.StreamRange { File = o.Data.File, Offset = o.Data.Offset, Bytes = o.Data.Bytes }
                }).Where(s => s.Bytes > 0).Select(s => (Object: o, Range: s))).ToArray();
                foreach (var streamGroup in ranges.GroupBy(r => r.Range.File))
                {
                    if (!fileSizes.TryGetValue(streamGroup.Key, out var file)) throw new InvalidDataException("流数据文件未在本次写出结果中找到：" + streamGroup.Key);
                    if (file.Bundle != group.Key) throw new InvalidDataException("流数据与对象不在同一 AB：" + streamGroup.Key);
                    foreach (var item in streamGroup)
                        if (item.Range.Offset < 0 || item.Range.Bytes < 0 || item.Range.Offset > file.Bytes - item.Range.Bytes)
                            throw new InvalidDataException("流数据超出写出文件范围：" + streamGroup.Key);
                    var events = streamGroup.SelectMany(r => new[] { (Position: r.Range.Offset, Object: r.Object, Change: 1), (Position: r.Range.Offset + r.Range.Bytes, Object: r.Object, Change: -1) })
                        .OrderBy(e => e.Position).GroupBy(e => e.Position);
                    var active = new Dictionary<WrittenObject, int>();
                    long begin = 0;
                    foreach (var boundary in events)
                    {
                        long length = boundary.Key - begin;
                        var owners = active.Keys.ToArray();
                        if (length > 0 && owners.Length > 0)
                        {
                            if (owners.Length == 1 || owners.All(o => o.Path != null && o.Key == owners[0].Key)) AddBytes(owners[0], length, resources, generated);
                            else
                            {
                                AddGenerated(generated, "共享数据区间", length);
                                foreach (var owner in owners.Where(o => o.Path != null)) { MarkPartial(owner, "物理数据由多个资源共享，其大小单列"); AddBytes(owner, 0, resources, generated); }
                            }
                        }
                        foreach (var change in boundary)
                        {
                            int count = (active.TryGetValue(change.Object, out var prior) ? prior : 0) + change.Change;
                            if (count == 0) active.Remove(change.Object); else active[change.Object] = count;
                        }
                        begin = boundary.Key;
                    }
                }
                long physical = group.Sum(f => f.Bytes);
                long assigned = resources.Values.Sum(r => r.bytes) + generated.Values.Sum();
                if (assigned > physical) throw new InvalidDataException("资源统计超过包内实际写出数据：" + group.Key);
                output.Add(new BundleSizeRecord { name = group.Key, unpackedBytes = physical, unassignedBytes = physical - assigned,
                    resources = resources.Values.OrderBy(r => r.path, StringComparer.Ordinal).ToArray(),
                    generated = generated.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new GeneratedSizeRecord { description = g.Key, bytes = g.Value }).ToArray() });
            }
            return output.ToArray();
        }
        static string MergeReasons(string first, string second) => string.Join("；", new[] { first, second }.Where(s => !string.IsNullOrEmpty(s)).SelectMany(s => s.Split('；')).Distinct());
        static void MarkPartial(WrittenObject obj, string note) => obj.Data.Note = MergeReasons(obj.Data.Note, note);
        static void AddGenerated(Dictionary<string, long> generated, string name, long bytes) => generated[name] = (generated.TryGetValue(name, out var prior) ? prior : 0) + bytes;
        static void AddBytes(WrittenObject obj, long bytes, Dictionary<string, ResourceSizeRecord> resources, Dictionary<string, long> generated)
        {
            if (obj.Path == null) { if (bytes > 0) AddGenerated(generated, "生成/内置 " + obj.Data.Type, bytes); return; }
            if (!resources.TryGetValue(obj.Key, out var resource)) resources.Add(obj.Key, resource = new ResourceSizeRecord { guid = obj.Guid, path = obj.Path, coverage = AssetBundleSizeCoverage.Complete });
            resource.bytes += bytes;
            if (!string.IsNullOrEmpty(obj.Data.Note)) { resource.coverage = AssetBundleSizeCoverage.Partial; resource.reason = MergeReasons(resource.reason, obj.Data.Note); }
        }
    }
}
