using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace July.Physics.Authoring
{
    public sealed class RepresentativeColliderTests
    {
        [Serializable]
        private sealed class Input
        {
            public Vector3[] vertices;
            public int[] triangles;
        }

        [TestCase("backpack")]
        [TestCase("bowl")]
        [TestCase("mug")]
        [TestCase("u_shape")]
        public void BothGoalsRunFromRawMesh(string fixture)
        {
            var directory = Path.Combine(Application.dataPath, "TestInputs");
            if (!Directory.Exists(directory))
                Assert.Ignore("Representative geometry fixtures are only installed in the isolated verification project.");
            var input = JsonUtility.FromJson<Input>(File.ReadAllText(Path.Combine(directory, fixture + ".json")));
            var geometry = new ColliderGeometry.Geometry{Vertices = input.vertices, Indices = input.triangles};
            foreach (var goal in new[]{ColliderGeometryGoal.Envelope, ColliderGeometryGoal.Structure})
            {
                var settings = ColliderGenerationSettings.ForGoal(goal);
                var fit = PrimitiveColliderFitter.Fit(geometry, settings, null, default);
                Assert.That(fit.Shapes.Length, Is.GreaterThan(0));
                if (goal == ColliderGeometryGoal.Envelope) Assert.That(fit.Shapes.Length, Is.LessThanOrEqualTo(3));
                Assert.That(fit.Shapes.All(s => Enum.IsDefined(typeof(PrimitiveColliderFitter.Kind), s.Type)));
                Assert.That(fit.Shapes.Length, Is.LessThanOrEqualTo(settings.TargetCount));
                Debug.Log(fixture + " " + goal + " " + fit.Note);
            }
        }
    }
}
