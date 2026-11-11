using System;
using System.IO;
using System.Linq;
using System.Threading;
using July.Physics.Authoring.PickingProxy;
using July.Physics.Authoring.PickingProxy.Gpu;
using UnityEngine;

namespace July.Physics.Authoring
{
    public static partial class ColliderGeneration
    {
        private static void ValidatePickingBudget(ColliderGenerationSettings settings)
        {
            if (settings.TargetCount > GenerationSettings.ConvexCountLimit)
                throw new ArgumentException("结构贴合最多16个形状。已有候选保留，请先将本栏数量调整为1至16再生成。");
        }

        private static string ReadPickingProxyKernel()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ColliderGeneration).Assembly);
            return File.ReadAllText(Path.Combine(package.resolvedPath, "Editor/PickingProxy/Gpu/Visibility.hlsl.txt"));
        }

        private static PrimitiveColliderFitter.Result FitPickingProxy(ColliderGeometry.Geometry geometry, string sourceHash,
            ColliderGenerationSettings settings, string kernel, bool rebuild, ColliderFitProgress progress, CancellationToken token)
        {
            var source = new TriangleMesh(geometry.Vertices.Select(p => new Vec3(p.x, p.y, p.z)).ToArray(), geometry.Indices);
            var options = new GenerationSettings { MaxConvexes = settings.TargetCount,
                MaxHullVertices = settings.MaxHullVertices, MaxInwardError = settings.PickingInwardErrorPercent / 100.0,
                Resolution = 64, LatitudeSamples = 13, LongitudeSamples = 24 };
            void Report(string stage)
            {
                switch (stage)
                {
                    case "Rasterizing source solid": progress.Report(.02f, "分析模型实体"); break;
                    case "Classifying source visibility patches": progress.Report(.10f, "分析孔隙与开放结构"); break;
                    case "Gating common space and preserving witness paths": progress.Report(.25f, "保留可穿过的空间"); break;
                    case "Abstracting common-space boundary": progress.Report(.30f, "概括开放空间边界"); break;
                    case "Filling local rim-bounded pockets": progress.Report(.34f, "填充不必要的凹陷"); break;
                    case "Building source-only common-cavity paths": progress.Report(.38f, "保护明显开放腔体"); break;
                    case "Building source-only wide through-space": progress.Report(.42f, "保护明显通路"); break;
                    case "Exporting and cleaning convex meshes": progress.Report(.98f, "压缩未替换的凸包顶点"); break;
                    case "Using cached analysis": progress.Report(.45f, "复用模型分析与拟合样本"); break;
                    case "Using cached fit": progress.Report(.94f, "复用原始拟合结果"); break;
                    case "Approximating native shapes": progress.Report(.95f, "先尝试原生基础形状"); break;
                    case "Validating mixed shapes": progress.Report(.99f, "检查混合拾取形状"); break;
                    default:
                        if (stage.StartsWith("Fit ", StringComparison.Ordinal))
                        {
                            int end = stage.IndexOf('/');
                            int step = int.Parse(stage.Substring(4, end - 4), System.Globalization.CultureInfo.InvariantCulture);
                            progress.Report(.50f + .45f * step / options.FitSteps, $"拟合拾取形状 {step}/{options.FitSteps}");
                        }
                        break;
                }
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var cache = new PickingProxyStageCache(sourceHash, options, kernel);
            var fit = rebuild ? null : cache.LoadFit(token); string route;
            if (fit != null) { Report("Using cached fit"); route = "复用拟合"; }
            else
            {
                var analysis = rebuild ? null : cache.LoadAnalysis(token);
                if (analysis == null)
                {
                    D3D11VisibilityBackend.TryCreate(kernel, out var backend, out var reason);
                    Debug.Log("[PickingProxy] " + reason);
                    analysis = PickingProxyGenerator.Analyze(source, options, token, Report, backend);
                    cache.SaveAnalysis(analysis, token);
                    route = backend == null ? "CPU" : "GPU可见性分析";
                }
                else { Report("Using cached analysis"); route = "复用分析／CPU拟合"; }
                fit = PickingProxyGenerator.Fit(analysis, options, token, Report).Fit;
                // Preserve the unsimplified fit even when a later vertex/error
                // budget fails, so adjusting that budget only repeats export.
                cache.SaveFit(fit, token);
            }
            var approximation = PickingPrimitiveApproximation.Export(fit, settings, token, Report);
            var shapes = approximation.Shapes;
            return new PrimitiveColliderFitter.Result { Shapes = shapes, GenerationSeconds = clock.Elapsed.TotalSeconds,
                Note = $"拾取结构贴合 · {route}：{shapes.Length}个形状，上限{settings.TargetCount}个。" +
                    $"\n{PrimitiveColliderFitter.DescribeComposition(shapes)}。" +
                    (settings.PickingShapes == PickingShapeMode.PreferPrimitives ? $"原生替换{approximation.Replaced}个，最大形状偏差{approximation.MaximumDeviation*100:0.###}%；偏差预算{settings.NativeShapeErrorPercent:0.##}%，点击范围变化预算{settings.NativeClickChangePercent:0.##}%。" : "仅保留凸包。") +
                    $"\n剩余凸包每包≤{settings.MaxHullVertices}顶点，凸网格整组≤{settings.TargetCount * settings.MaxHullVertices}顶点；允许内缩≤最长边的{settings.PickingInwardErrorPercent:0.##}%。" +
                    "\n原生形状允许容差内内缩、外扩和边界点击变化；请检查主体覆盖、孔口与明显背腔，再应用候选。" };
        }

        // Cached previews are file input. Validate their raw convex geometry before
        // passing it to Unity, while retaining all existing six-primitive caches.
        private static void ValidatePickingMesh(PrimitiveColliderFitter.Shape shape)
        {
            if (shape.ConvexVertices == null || shape.ConvexVertices.Length < 4 || shape.ConvexVertices.Length > 255 ||
                shape.ConvexVertices.Any(p => !Finite(p)) || shape.ConvexTriangles == null ||
                shape.ConvexTriangles.Length < 12 || shape.ConvexTriangles.Length > 254 * 3 || shape.ConvexTriangles.Length % 3 != 0 ||
                shape.ConvexTriangles.Any(i => i < 0 || i >= shape.ConvexVertices.Length))
                throw new InvalidDataException("缓存拾取凸网格无效。");
            var mesh = new TriangleMesh(shape.ConvexVertices.Select(p => new Vec3(p.x, p.y, p.z)).ToArray(), shape.ConvexTriangles);
            var normalized = PickingProxyGenerator.PrepareSource(mesh);
            double magnitude = shape.ConvexVertices.Max(p => Math.Max(Math.Abs((double)p.x), Math.Max(Math.Abs((double)p.y), Math.Abs((double)p.z))));
            double maxEdgeError = 2 * Math.Sqrt(3) * magnitude * 1.1920928955078125e-7 / normalized.Scale;
            try { ConvexMeshExport.Validate(normalized.Mesh, maxEdgeError); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
            { throw new InvalidDataException("缓存拾取网格不是闭合凸体。", error); }
        }
    }
}
