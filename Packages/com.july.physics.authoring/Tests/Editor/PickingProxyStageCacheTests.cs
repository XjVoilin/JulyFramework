using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using July.Physics.Authoring.PickingProxy;
using NUnit.Framework;
using UnityEngine;

namespace July.Physics.Authoring
{
    public sealed class PickingProxyStageCacheTests
    {
        string folder;
        readonly List<string> warnings = new List<string>();
        static readonly string Source = new string('A',64);
        static GenerationSettings Options() => new GenerationSettings { MaxConvexes = 2 };
        [SetUp] public void Setup()
        { folder = Path.Combine(Path.GetTempPath(), "JulyProxyStages-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); warnings.Clear(); }
        [TearDown] public void Cleanup() { Directory.Delete(folder, true); }
        PickingProxyStageCache Cache(GenerationSettings settings = null, string kernel = "kernel", string algorithm = "version1", string source = null) =>
            new PickingProxyStageCache(source ?? Source, settings ?? Options(), kernel, folder, algorithm, warnings.Add);
        static ProxyAnalysis Analysis()
        {
            var hull = ConvexHull.Build((from x in new[]{-.25,.25} from y in new[]{-.25,.25} from z in new[]{-.25,.25} select new Vec3(x,y,z)).ToArray());
            var data = new TrainingData { Body = hull.Mesh.Vertices, Fill = new[]{new Vec3()}, Negative = new[]{new Vec3(.4,.4,.4)},
                Rays = new[]{new TrainingRay(new Vec3(-1,0,0),new Vec3(1,0,0),.02,hull.Planes)}, HullPlanes = hull.Planes, Cell = 1.0/64 };
            return new ProxyAnalysis(new Vec3(1,2,3),3,hull.Mesh,hull.Planes,data);
        }
        static ProxyFit Fit(ProxyAnalysis a) => new ProxyFit(a.Center,a.Scale,new[]{a.Hull});
        static string Signature(TrainingData data)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            void Point(Vec3 p) { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
            foreach (var array in new[]{data.Body,data.Fill,data.Negative}) { writer.Write(array.Length); foreach (var p in array) Point(p); }
            foreach (var ray in data.Rays)
            { Point(ray.Start); Point(ray.End); writer.Write(ray.Radius); writer.Write(ray.Length); writer.Write(ray.HEnter); writer.Write(ray.HLeave); writer.Write(ray.HParallelOutside); }
            writer.Write(data.Cell); foreach (var plane in data.HullPlanes) { Point(plane.Normal); writer.Write(plane.Offset); }
            writer.Flush(); using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
        }

        [Test] public void AnalysisAndUnsimplifiedFitRoundTripWithoutChangingGeometry()
        {
            var original = Analysis(); var cache = Cache(); cache.SaveAnalysis(original,default); cache.SaveFit(Fit(original),default);
            var analysis = Cache().LoadAnalysis(default); var fit = Cache().LoadFit(default);
            Assert.That(Signature(analysis.Training), Is.EqualTo(Signature(original.Training)));
            Assert.That(fit.Scale, Is.EqualTo(original.Scale)); Assert.That(fit.Center.X, Is.EqualTo(original.Center.X));
            CollectionAssert.AreEqual(original.Hull.Indices,fit.Meshes[0].Indices);
            CollectionAssert.AreEqual(original.Hull.Vertices.Select(p=>(p.X,p.Y,p.Z)),fit.Meshes[0].Vertices.Select(p=>(p.X,p.Y,p.Z)));
            Assert.That(warnings, Is.Empty);
        }
        [Test] public void ExportBudgetReusesFitButCountAndFitStepsOnlyReuseAnalysis()
        {
            var original = Analysis(); Cache().SaveAnalysis(original,default); Cache().SaveFit(Fit(original),default);
            var options = Options(); options.MaxHullVertices = 24; options.MaxInwardError = .2; options.Workers = 1;
            Assert.That(Cache(options).LoadFit(default), Is.Not.Null);
            options.MaxConvexes = 3;
            Assert.That(Cache(options).LoadFit(default), Is.Null); Assert.That(Cache(options).LoadAnalysis(default), Is.Not.Null);
            options = Options(); options.FitSteps--;
            Assert.That(Cache(options).LoadFit(default), Is.Null); Assert.That(Cache(options).LoadAnalysis(default), Is.Not.Null);
        }
        [Test] public void DefaultAlgorithmIdentityAndKernelLineEndingsSurviveCopyToAnotherWorkstation()
        {
            string producer = Path.Combine(folder, "producer"), consumer = Path.Combine(folder, "consumer");
            var writer = new PickingProxyStageCache(Source, Options(), "kernel\r\nline", producer, warning: warnings.Add);
            var original = Analysis(); writer.SaveAnalysis(original, default); writer.SaveFit(Fit(original), default);
            Directory.CreateDirectory(Path.Combine(consumer, Source));
            foreach (var path in Directory.GetFiles(Path.Combine(producer, Source)))
                File.Copy(path, Path.Combine(consumer, Source, Path.GetFileName(path)));
            Directory.Delete(producer, true);
            var reader = new PickingProxyStageCache(Source, Options(), "kernel\nline", consumer, warning: warnings.Add);
            Assert.IsNotNull(reader.LoadAnalysis(default)); Assert.IsNotNull(reader.LoadFit(default));
            Assert.That(warnings, Is.Empty);
        }

        [Test] public void SourceAnalysisOptionsKernelAndAlgorithmInvalidateStages()
        {
            var original = Analysis(); Cache().SaveAnalysis(original,default); Cache().SaveFit(Fit(original),default);
            var variants = new List<PickingProxyStageCache> { Cache(source:new string('B',64)), Cache(kernel:"new kernel"), Cache(algorithm:"version2") };
            var resolution = Options(); resolution.Resolution++; variants.Add(Cache(resolution));
            var seed = Options(); seed.Seed++; variants.Add(Cache(seed));
            var views = Options(); views.TrainingViews++; variants.Add(Cache(views));
            foreach (var cache in variants) { Assert.That(cache.LoadAnalysis(default), Is.Null); Assert.That(cache.LoadFit(default), Is.Null); }
            Assert.That(warnings, Is.Empty, "normal invalidation is a cache miss");
        }
        [Test] public void SixteenRawHullsCanBeSavedAndRestored()
        {
            var original=Analysis();var options=Options();options.MaxConvexes=16;
            var fit=new ProxyFit(original.Center,original.Scale,Enumerable.Repeat(original.Hull,16).ToArray());
            var cache=Cache(options);cache.SaveFit(fit,default);
            Assert.That(cache.LoadFit(default).Meshes.Length,Is.EqualTo(16));Assert.That(warnings,Is.Empty);
        }
        [Test] public void CorruptOrTruncatedFilesWarnAndRecomputeWithoutLosingCandidates()
        {
            var original = Analysis(); var cache = Cache(); cache.SaveAnalysis(original,default);
            string path = Path.Combine(folder,Source,"analysis.bin"); var bytes = File.ReadAllBytes(path); bytes[bytes.Length-40] ^= 1; File.WriteAllBytes(path,bytes);
            Assert.That(cache.LoadAnalysis(default), Is.Null); Assert.That(warnings.Count, Is.EqualTo(1));
            cache.SaveAnalysis(original,default); Assert.That(cache.LoadAnalysis(default), Is.Not.Null);
            File.WriteAllBytes(path,new byte[]{1,2}); Assert.That(cache.LoadAnalysis(default), Is.Null);
            Assert.That(Directory.GetFiles(folder,"*.tmp",SearchOption.AllDirectories), Is.Empty);
        }
        [Test] public void CancelledWritesLeaveExistingStageAndNoTemporaryFiles()
        {
            var original = Analysis(); var cache = Cache(); cache.SaveFit(Fit(original),default);
            var bytes = File.ReadAllBytes(Path.Combine(folder,Source,"fit-2.bin")); var cancelled = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => cache.LoadFit(cancelled));
            Assert.Throws<OperationCanceledException>(() => cache.SaveFit(Fit(original),cancelled));
            CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Path.Combine(folder,Source,"fit-2.bin")));
            Assert.That(Directory.GetFiles(folder,"*.tmp",SearchOption.AllDirectories), Is.Empty);
        }
        [Test] public void UnwritableCacheIsObservableAndDoesNotFailTheGeneration()
        {
            string blocked = Path.Combine(folder,"file-not-directory"); File.WriteAllText(blocked,"occupied");
            var cache = new PickingProxyStageCache(Source,Options(),"kernel",blocked,"version1",warnings.Add);
            Assert.DoesNotThrow(() => cache.SaveFit(Fit(Analysis()),default)); Assert.That(warnings, Is.Not.Empty);
        }
        [Test] public void FailedVertexBudgetCanReexportTheSameRawFit()
        {
            var original = Analysis(); Cache().SaveFit(Fit(original),default);
            var options = Options(); options.MaxHullVertices = 4; options.MaxInwardError = 0;
            Assert.Throws<InvalidOperationException>(() => PickingProxyGenerator.Export(Cache(options).LoadFit(default),options,default,null));
            options.MaxHullVertices = 8;
            var meshes = PickingProxyGenerator.Export(Cache(options).LoadFit(default),options,default,null);
            Assert.That(meshes.Single().Vertices.Length, Is.EqualTo(8));
        }
        [Test] public void ParallelViewsKeepTrainingSamplesAndRayOrderIdentical()
        {
            var mesh = UShape();
            var options = new GenerationSettings { MaxConvexes = 3, Resolution = 16, LatitudeSamples = 5, LongitudeSamples = 8, TrainingViews = 9, ProjectionPixels = 32, Workers = 1 };
            var serial = PickingProxyGenerator.Analyze(mesh,options,default,null,null);
            options.Workers = 8;
            var parallel = PickingProxyGenerator.Analyze(mesh,options,default,null,null);
            Assert.That(serial.Training.Rays.Length, Is.GreaterThan(0));
            Assert.That(Signature(parallel.Training), Is.EqualTo(Signature(serial.Training)));
        }
        static TriangleMesh UShape()
        {
            var xy = new[]{(-.5,-.5),(.5,-.5),(.5,.5),(.25,.5),(.25,-.25),(-.25,-.25),(-.25,.5),(-.5,.5)};
            var vertices = new[]{-.1,.1}.SelectMany(z=>xy.Select(p=>new Vec3(p.Item1,p.Item2,z))).ToArray();
            var cap = new[]{0,1,4,0,4,5,1,2,3,1,3,4,0,5,6,0,6,7}; var indices = new List<int>();
            for(int i=0;i<cap.Length;i+=3) { indices.AddRange(new[]{cap[i+2],cap[i+1],cap[i]}); indices.AddRange(new[]{cap[i]+8,cap[i+1]+8,cap[i+2]+8}); }
            for(int i=0;i<8;i++) { int j=(i+1)%8; indices.AddRange(new[]{i,j,j+8,i,j+8,i+8}); }
            return new TriangleMesh(vertices,indices.ToArray());
        }
    }
}
