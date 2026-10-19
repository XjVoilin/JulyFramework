using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring.Tests
{
    public sealed class ColliderGenerationTests
    {
        private GameObject _root;
        private Transform _output;
        private ColliderGenerationRequest _request;
        [SetUp] public void SetUp()
        {
            _root = new GameObject("PrimitiveFitFixture");
            _output = new GameObject("Shapes").transform;
            _output.SetParent(_root.transform, false);
            _output.gameObject.AddComponent<BoxCollider>();
            _request = new ColliderGenerationRequest { OutputRoot = _output, Layer = 17 };
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_root); }
        private GameObject Source(PrimitiveType type, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            _request.Sources = ColliderGeometry.Scan(_root.transform);
            return go;
        }
        private IEnumerator Fit(Action<ColliderGenerationResult> check)
        {
            var meshes = Resources.FindObjectsOfTypeAll<Mesh>().Select(m => m.GetInstanceID()).ToArray();
            var assets = AssetDatabase.GetAllAssetPaths();
            var task = ColliderGeneration.GenerateAsync(_request);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.Result.Colliders.All(c => c is BoxCollider || c is SphereCollider || c is CapsuleCollider));
            Assert.That(task.Result.Colliders.Length, Is.InRange(1, _request.Settings.MaxShapes));
            Assert.That(Resources.FindObjectsOfTypeAll<Mesh>().Select(m => m.GetInstanceID()).Except(meshes), Is.Empty, "No new Mesh objects, including temporary outputs.");
            Assert.That(AssetDatabase.GetAllAssetPaths().Except(assets), Is.Empty, "No generated assets.");
            check(task.Result);
        }
        [UnityTest] public IEnumerator BoxChoosesOneBox()
        {
            Source(PrimitiveType.Cube, Vector3.zero, new Vector3(2, 1, 0.5f));
            yield return Fit(r => { Assert.That(r.Colliders, Has.Length.EqualTo(1)); Assert.That(r.Colliders[0], Is.TypeOf<BoxCollider>()); Assert.That(r.MeetsTolerance); });
        }
        [UnityTest] public IEnumerator SphereChoosesOneSphere()
        {
            Source(PrimitiveType.Sphere, Vector3.zero, Vector3.one);
            yield return Fit(r => { Assert.That(r.Colliders, Has.Length.EqualTo(1)); Assert.That(r.Colliders[0], Is.TypeOf<SphereCollider>()); });
        }
        [UnityTest] public IEnumerator CapsuleChoosesOneCapsule()
        {
            Source(PrimitiveType.Capsule, Vector3.zero, Vector3.one);
            yield return Fit(r => { Assert.That(r.Colliders, Has.Length.EqualTo(1)); Assert.That(r.Colliders[0], Is.TypeOf<CapsuleCollider>()); });
        }
        [UnityTest] public IEnumerator SeparatedSourcesShareBudgetAndPreserveGap()
        {
            Source(PrimitiveType.Cube, Vector3.left * 1.5f, Vector3.one);
            Source(PrimitiveType.Cube, Vector3.right * 1.5f, Vector3.one);
            yield return Fit(r => {
                Assert.That(r.Colliders, Has.Length.EqualTo(2));
                Assert.That(r.Colliders.All(c => Vector3.Distance(c.ClosestPoint(Vector3.zero), Vector3.zero) > 0.8f));
            });
            _request.Settings.MaxShapes = 1;
            yield return Fit(r => { Assert.That(r.Colliders, Has.Length.EqualTo(1)); Assert.That(r.MeetsTolerance, Is.False); });
        }
        [UnityTest] public IEnumerator RingKeepsClickableHoleWithFewShapes()
        {
            Source(PrimitiveType.Cube, Vector3.left * 1.5f, new Vector3(0.5f, 3.5f, 0.5f));
            Source(PrimitiveType.Cube, Vector3.right * 1.5f, new Vector3(0.5f, 3.5f, 0.5f));
            Source(PrimitiveType.Cube, Vector3.up * 1.5f, new Vector3(2.5f, 0.5f, 0.5f));
            Source(PrimitiveType.Cube, Vector3.down * 1.5f, new Vector3(2.5f, 0.5f, 0.5f));
            _request.Settings.MaxShapes = 8;
            yield return Fit(r => {
                UnityEngine.Physics.SyncTransforms();
                Assert.That(r.Colliders.Length, Is.InRange(2, 8));
                var ray = new Ray(Vector3.back * 5, Vector3.forward);
                Assert.That(r.Colliders.Any(c => c.Raycast(ray, out _, 10)), Is.False, "The center hole must not intercept a click.");
            });
        }
        [UnityTest] public IEnumerator TransformsAndUnreadableMeshArePreserved()
        {
            var source = Source(PrimitiveType.Cube, new Vector3(2, 3, -1), new Vector3(-2, 0.5f, 1));
            source.transform.localRotation = Quaternion.Euler(12, 37, 25);
            var original = source.GetComponent<MeshFilter>().sharedMesh;
            var clone = Object.Instantiate(original);
            source.GetComponent<MeshFilter>().sharedMesh = clone;
            clone.UploadMeshData(true);
            try
            {
                _request.Sources = ColliderGeometry.Scan(_root.transform);
                yield return Fit(r => {
                    Assert.That(clone.isReadable, Is.False);
                    foreach (var v in original.vertices)
                    {
                        var point = source.transform.TransformPoint(v);
                        Assert.That(r.Colliders.Min(c => Vector3.Distance(c.ClosestPoint(point), point)), Is.LessThan(0.08f));
                    }
                });
            }
            finally { Object.DestroyImmediate(clone); }
        }
        [UnityTest] public IEnumerator RegenerationUndoRedoAndBindingRollbackPreserveManualPose()
        {
            Source(PrimitiveType.Cube, Vector3.zero, Vector3.one);
            yield return Fit(_ => { });
            _output.GetChild(0).localPosition += Vector3.up;
            var pose = _output.GetChild(0).localPosition;
            yield return Fit(_ => { });
            Undo.PerformUndo(); yield return null;
            Assert.That(_output.GetChild(0).localPosition, Is.EqualTo(pose));
            Undo.PerformRedo(); yield return null;
            var snapshot = _output.GetChild(0).localPosition;
            Action<ColliderGenerationResult> reject = r => { if (r.Request.OutputRoot == _output) throw new InvalidOperationException("Reject fixture"); };
            ColliderGeneration.Applied += reject;
            try
            {
                var task = ColliderGeneration.GenerateAsync(_request);
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsFaulted);
                Assert.That(task.Exception.GetBaseException().Message, Is.EqualTo("Reject fixture"));
                Assert.That(_output.childCount, Is.EqualTo(1));
                Assert.That(_output.GetChild(0).localPosition, Is.EqualTo(snapshot));
            }
            finally { ColliderGeneration.Applied -= reject; }
        }
        [UnityTest] public IEnumerator NonuniformOutputFailsBeforeReplacement()
        {
            Source(PrimitiveType.Cube, Vector3.zero, Vector3.one);
            _output.localScale = new Vector3(1, 2, 1);
            var task = ColliderGeneration.GenerateAsync(_request);
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsFaulted); Assert.That(task.Exception.GetBaseException(), Is.TypeOf<ArgumentException>());
            Assert.That(_output.GetComponent<BoxCollider>(), Is.Not.Null);
        }

        [UnityTest] public IEnumerator EmptyNamedOrRenamedGroupOpensAndGeneratesWithoutCollider()
        {
            Source(PrimitiveType.Cube, Vector3.zero, Vector3.one);
            Object.DestroyImmediate(_output.GetComponent<BoxCollider>());
            _output.name = "UserDefinedContainer";
            _output.gameObject.layer = 17;
            _root.SetActive(false);
            var sibling = new GameObject("KeepOtherPurpose");
            sibling.transform.SetParent(_root.transform, false);
            var untouched = sibling.AddComponent<SphereCollider>();
            untouched.radius = 2.7f;
            var marker = _output.gameObject.AddComponent<ColliderAuthoringTarget>();
            var editor = UnityEditor.Editor.CreateEditor(marker);
            Assert.That(editor.GetType().Name, Is.EqualTo("ColliderAuthoringInspector"));
            Object.DestroyImmediate(editor);
            ColliderAuthoringContext context = null;
            Action<ColliderAuthoringContext> capture = value => context = value;
            ColliderAuthoringContext.Opening += capture;
            try { ColliderAuthoringContext.Create(_output, new ColliderGenerationSettings()); }
            finally { ColliderAuthoringContext.Opening -= capture; }
            Assert.That(context.Request.OutputRoot, Is.SameAs(_output));
            Assert.That(context.Request.Layer, Is.EqualTo(17));
            Assert.That(context.Request.Sources.Count, Is.EqualTo(1));
            Assert.That(_output.GetComponentsInChildren<Collider>(true), Is.Empty);
            _request = context.Request;
            yield return Fit(result => {
                Assert.That(result.Colliders.All(shape => shape.gameObject.layer == 17));
                Assert.That(untouched.radius, Is.EqualTo(2.7f));
                Assert.That(_output.GetComponent<ColliderAuthoringTarget>(), Is.SameAs(marker));
            });
            _output.name = "RenamedAgain";
            _request = ColliderAuthoringContext.Create(_output, new ColliderGenerationSettings()).Request;
            yield return Fit(_ => Assert.That(untouched.radius, Is.EqualTo(2.7f)));
        }

        [Test] public void MixedGroupIdentifiesTheConflictingNodesAndSetting()
        {
            var child = new GameObject("DisabledShape");
            child.transform.SetParent(_output, false);
            child.AddComponent<BoxCollider>().enabled = false;
            var error = Assert.Throws<ArgumentException>(() => ColliderGeneration.CreateRequest(_output, new ColliderGenerationSettings()));
            Assert.That(error.Message, Does.Contain("DisabledShape").And.Contain("Enabled").And.Contain("Shapes"));
            Assert.That(_output.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(2));
        }

        [Test] public void MixedPurposeGroupIsRejectedWithoutChangingItsShapes()
        {
            var child = new GameObject("OtherLayer");
            child.transform.SetParent(_output, false);
            child.layer = 5;
            child.AddComponent<SphereCollider>();
            Assert.Throws<ArgumentException>(() => ColliderAuthoringContext.Create(_output, new ColliderGenerationSettings()));
            Assert.That(_output.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(2));
        }
    }
}
