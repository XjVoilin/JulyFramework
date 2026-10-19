using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Rendering.URP.Tests
{
    public sealed class MeshHighlightLifecycleTests
    {
        [UnityTest]
        public IEnumerator SelectionUsesSharedAssetsAndClearsWhenDisabled()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Compound highlight fixture");
            root.SetActive(false);
            var first = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var second = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            var target = root.AddComponent<MeshHighlightTarget>();
            var material = new Material(Shader.Find("July/URP/Mesh Highlight")) { enableInstancing = true };
            var renderers = new[] { first.GetComponent<MeshRenderer>(), second.GetComponent<MeshRenderer>() };
            var mesh = first.GetComponent<MeshFilter>().sharedMesh;
            var surfaceMaterial = renderers[0].sharedMaterial;
            var data = new SerializedObject(target);
            var array = data.FindProperty("_renderers");
            array.arraySize = renderers.Length;
            for (var i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            data.FindProperty("_material").objectReferenceValue = material;
            data.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            Assert.IsFalse(target.IsHighlighted);
            target.SetHighlighted(true);
            Assert.IsTrue(target.IsHighlighted);
            Assert.AreSame(mesh, first.GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(surfaceMaterial, renderers[0].sharedMaterial);
            Assert.IsFalse(renderers[0].HasPropertyBlock());
            target.SetHighlighted(false);
            Assert.IsFalse(target.IsHighlighted);
            target.SetHighlighted(true);
            root.SetActive(false);
            Assert.IsFalse(target.IsHighlighted);
            root.SetActive(true);
            Assert.IsFalse(target.IsHighlighted, "Re-enabling a hidden object must not restore stale selection.");
            target.SetHighlighted(true);
            target.enabled = false;
            target.enabled = true;
            Assert.IsFalse(target.IsHighlighted, "Component disable clears its own transient highlight state.");
            var cameraObject = new GameObject("Camera fixture", typeof(Camera));
            var source = cameraObject.AddComponent<MeshHighlightCamera>();
            source.Target = target;
            source.enabled = false;
            Assert.IsNull(source.Target);
            source.enabled = true;
            Assert.IsNull(source.Target);
            source.Target = target;
            Object.Destroy(root);
            yield return null;
            Assert.IsTrue(source.Target == null, "A destroyed candidate retains Unity's native null semantics.");
            source.Target = null;
            Object.Destroy(cameraObject);
            Object.Destroy(material);
            yield return new ExitPlayMode();
        }
    }
}
