using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    public sealed class LegacyColliderCleanupTests
    {
        [TestCase("FineColliderPointMesh_old")]
        [TestCase("FineSphereEdited_old")]
        [TestCase("g110_collider_hemisphere")]
        [TestCase("g110_collider_cylinder")]
        public void OldDisabledSourceDoesNotOverrideFinalColliderSettings(string meshName)
        {
            var root = new GameObject("old-handmade-output");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(primitive);
            mesh.name = meshName;
            try
            {
                var source = root.AddComponent<SphereCollider>(); source.enabled = false; source.isTrigger = true;
                var final = root.AddComponent<MeshCollider>(); final.sharedMesh = mesh; final.convex = true; final.contactOffset = .027f;
                var request = ColliderGeneration.CreateRequest(root.transform, ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure));
                Assert.That(request.Enabled, Is.True);
                Assert.That(request.IsTrigger, Is.False);
                Assert.That(request.ContactOffset, Is.EqualTo(final.contactOffset));
                Assert.That(source.enabled, Is.False); Assert.That(final.sharedMesh, Is.SameAs(mesh));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }

        [UnityTest] public IEnumerator ExistingHandmadeMeshNeedsNoAuthoringComponentAndKeepsItsShape()
        {
            var host = new GameObject("legacy-item");
            var root = new GameObject("Colliders"); root.transform.SetParent(host.transform, false);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(primitive);
            try
            {
                var part = new GameObject("existing-part"); part.transform.SetParent(root.transform, false);
                part.transform.localScale = new Vector3(2, 3, 4);
                var final = part.AddComponent<MeshCollider>(); final.sharedMesh = mesh; final.convex = true;
                var request = ColliderGeneration.CreateRequest(root.transform, ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure));
                Assert.That(request.Enabled, Is.True);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty);
                Assert.That(part.transform.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
                Assert.That(final.sharedMesh, Is.SameAs(mesh));
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(mesh); }
        }
    }
}
