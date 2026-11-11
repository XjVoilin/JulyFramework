using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using July.Physics.Authoring.PickingProxy;
using UnityEngine;

namespace July.Physics.Authoring
{
    // Local, disposable computation data; durable candidates and author records live elsewhere.
    // One analysis and at most sixteen raw fits per geometry; parameter edits
    // overwrite the matching stage slot rather than accumulating versions.
    internal sealed class PickingProxyStageCache
    {
        // Bump when analysis/fitting behavior or parameter semantics change; never use a per-build assembly MVID.
        const string AlgorithmVersion = "picking-proxy-stages-v1";
        const int Magic = 0x4A505343, Version = 1, MaxBytes = 128 * 1024 * 1024;
        readonly string directory, analysisKey, fitKey;
        readonly int count;
        readonly Action<string> warning;
        internal PickingProxyStageCache(string sourceHash, GenerationSettings settings, string kernel,
            string root = "Library/July/ColliderAuthoring/PickingProxyStages", string algorithm = null, Action<string> warning = null)
        {
            this.warning = warning ?? (message => Debug.LogWarning(message));
            directory = Path.Combine(root, sourceHash); count = settings.MaxConvexes;
            // A stable algorithm revision permits reuse after recompilation or moving the cache.
            // Normalize text line endings so Git checkout settings do not invalidate the kernel.
            string identity = algorithm ?? AlgorithmVersion;
            analysisKey = Hash(string.Join("|", identity, Hash(kernel.Replace("\r\n", "\n")), sourceHash, settings.Resolution,
                settings.LatitudeSamples, settings.LongitudeSamples, settings.Seed, settings.TrainingViews, settings.ProjectionPixels));
            fitKey = Hash(string.Join("|", analysisKey, settings.MaxConvexes, settings.FitSteps));
        }
        static string Hash(string value)
        { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        string AnalysisPath => Path.Combine(directory, "analysis.bin");
        string FitPath => Path.Combine(directory, "fit-" + count + ".bin");

        internal ProxyAnalysis LoadAnalysis(CancellationToken token) => Read(AnalysisPath, 1, analysisKey, reader =>
        {
            var center = Point(reader); double scale = Positive(reader);
            var hull = Mesh(reader); var planes = new Plane3[Count(reader, 32)];
            if (planes.Length < 4) throw new InvalidDataException("缓存凸体平面不足。");
            for (int i = 0; i < planes.Length; i++) planes[i] = new Plane3(Point(reader), Number(reader));
            double cell = Positive(reader); var body = Points(reader); var fill = Points(reader); var negative = Points(reader);
            var rays = new TrainingRay[Count(reader, 56)];
            for (int i = 0; i < rays.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var start = Point(reader); var end = Point(reader); double radius = Number(reader);
                if (radius < 0 || (end-start).LengthSquared == 0) throw new InvalidDataException("缓存射线无效。");
                rays[i] = new TrainingRay(start, end, radius, planes);
            }
            return new ProxyAnalysis(center, scale, hull, planes,
                new TrainingData { Body = body, Fill = fill, Negative = negative, Rays = rays, HullPlanes = planes, Cell = cell });
        }, token);
        internal ProxyFit LoadFit(CancellationToken token) => Read(FitPath, 2, fitKey, reader =>
        {
            var center = Point(reader); double scale = Positive(reader); int length = Count(reader, 1);
            if (length < 1 || length > count || length > GenerationSettings.ConvexCountLimit)
                throw new InvalidDataException("缓存凸包数量超出预算。");
            var meshes = new TriangleMesh[length];
            for (int i = 0; i < length; i++) { token.ThrowIfCancellationRequested(); meshes[i] = Mesh(reader); }
            return new ProxyFit(center, scale, meshes);
        }, token);

        internal void SaveAnalysis(ProxyAnalysis value, CancellationToken token) => Write(AnalysisPath, 1, analysisKey, writer =>
        {
            Point(writer, value.Center); writer.Write(value.Scale); Mesh(writer, value.Hull);
            writer.Write(value.HullPlanes.Length);
            foreach (var plane in value.HullPlanes) { Point(writer, plane.Normal); writer.Write(plane.Offset); }
            var data = value.Training; writer.Write(data.Cell); Points(writer, data.Body); Points(writer, data.Fill); Points(writer, data.Negative);
            writer.Write(data.Rays.Length);
            foreach (var ray in data.Rays) { token.ThrowIfCancellationRequested(); Point(writer, ray.Start); Point(writer, ray.End); writer.Write(ray.Radius); }
        }, token);
        internal void SaveFit(ProxyFit value, CancellationToken token) => Write(FitPath, 2, fitKey, writer =>
        {
            Point(writer, value.Center); writer.Write(value.Scale); writer.Write(value.Meshes.Length);
            foreach (var mesh in value.Meshes) { token.ThrowIfCancellationRequested(); Mesh(writer, mesh); }
        }, token);

        T Read<T>(string path, byte kind, string key, Func<BinaryReader,T> parse, CancellationToken token) where T : class
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return null;
            try
            {
                using var file = AuthoringFile.OpenRead(path); using var reader = new BinaryReader(file);
                if (file.Length > MaxBytes || reader.ReadInt32() != Magic || reader.ReadInt32() != Version || reader.ReadByte() != kind)
                    throw new InvalidDataException("阶段缓存格式无效。");
                var identity = reader.ReadBytes(64);
                if (identity.Length != 64) throw new InvalidDataException("阶段缓存标识不完整。");
                if (Encoding.ASCII.GetString(identity) != key) return null;
                int length = reader.ReadInt32();
                if (length < 0 || length != file.Length-file.Position-32) throw new InvalidDataException("阶段缓存长度无效。");
                var payload = reader.ReadBytes(length); var expected = reader.ReadBytes(32);
                using var hash = SHA256.Create();
                if (!hash.ComputeHash(payload).SequenceEqual(expected)) throw new InvalidDataException("阶段缓存校验失败。");
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(payload); using var data = new BinaryReader(stream);
                var value = parse(data);
                if (stream.Position != stream.Length) throw new InvalidDataException("阶段缓存有多余内容。");
                return value;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException || error is OverflowException)
            { warning("[PickingProxy] 阶段缓存不可用，重新计算：" + error.Message); return null; }
        }
        void Write(string path, byte kind, string key, Action<BinaryWriter> serialize, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream();
                using (var data = new BinaryWriter(stream, Encoding.UTF8, true)) serialize(data);
                if (stream.Length > MaxBytes-109) throw new IOException("阶段数据超过128MB，本次跳过缓存。");
                byte[] payload = stream.ToArray();
                AuthoringFile.WriteAtomic(path,temporary =>
                {
                    using (var file = File.Create(temporary))
                    using (var writer = new BinaryWriter(file))
                    {
                        writer.Write(Magic); writer.Write(Version); writer.Write(kind); writer.Write(Encoding.ASCII.GetBytes(key)); writer.Write(payload.Length);
                        writer.Write(payload); using var hash = SHA256.Create(); writer.Write(hash.ComputeHash(payload));
                    }
                    token.ThrowIfCancellationRequested();
                });
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { warning("[PickingProxy] 阶段缓存保存失败，将继续处理本次计算结果：" + error.Message); }
        }
        static int Count(BinaryReader reader, int bytes)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > (reader.BaseStream.Length-reader.BaseStream.Position)/bytes)
                throw new InvalidDataException("阶段缓存数组长度无效。");
            return count;
        }
        static double Number(BinaryReader reader)
        { double value = reader.ReadDouble(); if (!double.IsFinite(value)) throw new InvalidDataException("阶段缓存数值无效。"); return value; }
        static double Positive(BinaryReader reader)
        { double value = Number(reader); if (value <= 0) throw new InvalidDataException("阶段缓存尺度无效。"); return value; }
        static Vec3 Point(BinaryReader reader) => new Vec3(Number(reader), Number(reader), Number(reader));
        static void Point(BinaryWriter writer, Vec3 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
        static Vec3[] Points(BinaryReader reader)
        { var result = new Vec3[Count(reader, 24)]; for (int i = 0; i < result.Length; i++) result[i] = Point(reader); return result; }
        static void Points(BinaryWriter writer, Vec3[] points)
        { writer.Write(points.Length); foreach (var point in points) Point(writer, point); }
        static TriangleMesh Mesh(BinaryReader reader)
        {
            var vertices = Points(reader); var indices = new int[Count(reader, 4)];
            for (int i = 0; i < indices.Length; i++) indices[i] = reader.ReadInt32();
            return new TriangleMesh(vertices, indices);
        }
        static void Mesh(BinaryWriter writer, TriangleMesh mesh)
        { Points(writer, mesh.Vertices); writer.Write(mesh.Indices.Length); foreach (int index in mesh.Indices) writer.Write(index); }
    }
}
