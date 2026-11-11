using System;
using System.Linq;
using System.Threading;
using July.Physics.Authoring.PickingProxy;
using NUnit.Framework;
using UnityEngine;

namespace July.Physics.Authoring
{
    public sealed class ConvexVertexBudgetTests
    {
        static TriangleMesh Rounded() => ConvexHull.Build(Numerics.SphereDirections(96).Select(p => p * .4).ToArray()).Mesh;

        [TestCase(16)] [TestCase(24)] [TestCase(32)]
        public void BudgetProducesClosedInscribedHullWithMeasuredError(int budget)
        {
            var original = Rounded();
            var output = ConvexVertexBudget.Reduce(original, budget, .1);
            Assert.That(output.Vertices.Length, Is.LessThanOrEqualTo(budget));
            Assert.That(output.Indices.Length / 3, Is.LessThanOrEqualTo(2 * budget - 4));
            Assert.That(output.Vertices.All(v => original.Vertices.Any(p => (p-v).Length < 1e-9)), Is.True,
                "an inscribed hull cannot introduce new occupied space across an existing hole");
            var hull = ConvexHull.Build(output.Vertices);
            Assert.That(ConvexMeshExport.InwardDeviation(original.Vertices, output, hull.Planes), Is.LessThanOrEqualTo(.1));
            Assert.DoesNotThrow(() => ConvexMeshExport.Validate(output));
            var again = ConvexVertexBudget.Reduce(original, budget, .1);
            CollectionAssert.AreEqual(output.Indices, again.Indices);
            CollectionAssert.AreEqual(output.Vertices.Select(p => (p.X,p.Y,p.Z)), again.Vertices.Select(p => (p.X,p.Y,p.Z)));
        }

        [Test] public void TightErrorCannotSilentlyOverrideVertexBudget()
        {
            Assert.Throws<InvalidOperationException>(() => ConvexVertexBudget.Reduce(Rounded(), 4, .001));
            Assert.Throws<OperationCanceledException>(() => ConvexVertexBudget.Reduce(Rounded(), 32, .1, new CancellationToken(true)));
        }

        [Test] public void SimpleHullIsNotInflatedToUseEntireBudget()
        {
            var points = (from x in new[]{-.25,.25} from y in new[]{-.25,.25} from z in new[]{-.25,.25} select new Vec3(x,y,z)).ToArray();
            var original = ConvexHull.Build(points).Mesh;
            var output = ConvexVertexBudget.Reduce(original, 32, 0);
            Assert.That(output.Vertices.Length, Is.EqualTo(8));
            Assert.That(ConvexMeshExport.InwardDeviation(points, output, ConvexHull.Build(output.Vertices).Planes), Is.Zero);
        }

        [Test] public void OldDensePreviewStaysReadableButDoesNotMeetNewBudget()
        {
            var original = Rounded();
            var preview = Preview(original);
            var settings = ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure);
            var loaded = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded.MatchesSettings(settings), Is.False);
            preview = Preview(ConvexVertexBudget.Reduce(original, 16, .1));
            loaded = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
            Assert.That(loaded.MatchesSettings(settings), Is.True);
            var legacySettings = JsonUtility.FromJson<ColliderGenerationSettings>("{\"Goal\":1,\"TargetCount\":8}");
            Assert.That(legacySettings.MaxHullVertices, Is.EqualTo(16));
            Assert.That(legacySettings.PickingInwardErrorPercent, Is.EqualTo(10));
        }

        [Test] public void SixteenHullLimitRejectsGenerationAndOversizedCandidates()
        {
            Assert.That(new GenerationSettings().MaxConvexes, Is.EqualTo(8));
            Assert.Throws<ArgumentException>(() => PickingProxyGenerator.Generate(Rounded(), new GenerationSettings { MaxConvexes = 17 }));
            var small = ConvexVertexBudget.Reduce(Rounded(), 16, .1);
            var preview = Preview(small);
            preview.Settings.TargetCount = 16;
            preview.Fit.Shapes = Enumerable.Repeat(preview.Fit.Shapes[0], 17).ToArray();
            var loaded = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
            Assert.That(loaded.Count, Is.EqualTo(17));
            Assert.That(loaded.MatchesSettings(loaded.Settings), Is.False, "parameters must not bypass the sixteen-shape application limit");
            preview.Fit.Shapes = preview.Fit.Shapes.Take(16).ToArray();
            Assert.That(preview.MatchesSettings(preview.Settings), Is.True);
        }

        [Test] public void CurrentDefaultsAndNewBudgetBoundariesAreSharedByEditorAndCore()
        {
            var settings = ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure); var core = new GenerationSettings();
            Assert.That(settings.TargetCount,Is.EqualTo(8)); Assert.That(core.MaxConvexes,Is.EqualTo(8));
            Assert.That(settings.MaxHullVertices,Is.EqualTo(16)); Assert.That(core.MaxHullVertices,Is.EqualTo(16));
            Assert.That(settings.PickingInwardErrorPercent,Is.EqualTo(10)); Assert.That(core.MaxInwardError,Is.EqualTo(.1));
            Assert.That(settings.PickingShapes,Is.EqualTo(PickingShapeMode.PreferPrimitives));
            settings.MaxHullVertices=24;settings.PickingInwardErrorPercent=20;settings.TargetCount=16;
            Assert.DoesNotThrow(settings.Validate);Assert.That(settings.HasCurrentPickingBudget,Is.True);
            settings.MaxHullVertices=25;Assert.Throws<ArgumentException>(settings.Validate);Assert.That(settings.HasCurrentPickingBudget,Is.False);
            settings.MaxHullVertices=24;settings.PickingInwardErrorPercent=20.01f;Assert.Throws<ArgumentException>(settings.Validate);
            Assert.Throws<ArgumentException>(() => PickingProxyGenerator.Generate(Rounded(),new GenerationSettings{MaxHullVertices=25}));
            Assert.Throws<ArgumentException>(() => PickingProxyGenerator.Generate(Rounded(),new GenerationSettings{MaxInwardError=.201}));
        }

        [Test] public void PublicCoreAcceptsSixteenShapesAndTwentyPercentWithoutExceedingVertexBudget()
        {
            var output=PickingProxyGenerator.Generate(Rounded(),new GenerationSettings { MaxConvexes=16,MaxHullVertices=24,MaxInwardError=.2,
                Resolution=16,LatitudeSamples=5,LongitudeSamples=8,TrainingViews=9,ProjectionPixels=24,FitSteps=0,Workers=2 });
            Assert.That(output.RequestedMaximum,Is.EqualTo(16));Assert.That(output.ConvexMeshes.Length,Is.InRange(1,16));
            Assert.That(output.ConvexMeshes.All(m=>m.Vertices.Length<=24),Is.True);
        }

        [Test] public void OpenShapeCanActuallyFitSixteenRegions()
        {
            var xy=new[]{(-.5,-.5),(.5,-.5),(.5,.5),(.25,.5),(.25,-.25),(-.25,-.25),(-.25,.5),(-.5,.5)};
            var vertices=new[]{-.1,.1}.SelectMany(z=>xy.Select(p=>new Vec3(p.Item1,p.Item2,z))).ToArray();
            var cap=new[]{0,1,4,0,4,5,1,2,3,1,3,4,0,5,6,0,6,7};var indices=new System.Collections.Generic.List<int>();
            for(int i=0;i<cap.Length;i+=3){indices.AddRange(new[]{cap[i+2],cap[i+1],cap[i]});indices.AddRange(new[]{cap[i]+8,cap[i+1]+8,cap[i+2]+8});}
            for(int i=0;i<8;i++){int j=(i+1)%8;indices.AddRange(new[]{i,j,j+8,i,j+8,i+8});}
            var output=PickingProxyGenerator.Generate(new TriangleMesh(vertices,indices.ToArray()),new GenerationSettings
                {MaxConvexes=16,MaxHullVertices=24,MaxInwardError=.2,Resolution=16,LatitudeSamples=5,LongitudeSamples=8,
                 TrainingViews=9,ProjectionPixels=32,FitSteps=20,Workers=2});
            Assert.That(output.TrainingRayCount,Is.GreaterThan(0));Assert.That(output.FittedConvexCount,Is.EqualTo(16));
            Assert.That(output.ConvexMeshes.Length,Is.InRange(1,16));Assert.That(output.ConvexMeshes.All(m=>m.Vertices.Length<=24),Is.True);
        }

        static ColliderGeneration.Preview Preview(TriangleMesh mesh) => new ColliderGeneration.Preview
        {
            Settings = ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure),
            FitMatrix = Matrix4x4.identity, GeometryHash = new string('A',64),
            Fit = new PrimitiveColliderFitter.Result { Shapes = new[] { new PrimitiveColliderFitter.Shape
            {
                Type = PrimitiveColliderFitter.Kind.Convex,
                ConvexVertices = mesh.Vertices.Select(p => new Vector3((float)p.X,(float)p.Y,(float)p.Z)).ToArray(),
                ConvexTriangles = mesh.Indices
            } } }
        };
    }
}
