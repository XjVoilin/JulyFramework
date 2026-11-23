using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Rendering.URP.Editor
{
    internal sealed class MatCapPreview : IDisposable
    {
        sealed class DrawnRenderer
        {
            internal MeshRenderer renderer;
            internal Material[] original;
            internal Material[] withoutMatCap;
            internal Material[] withMatCap;
        }

        readonly PreviewRenderUtility preview = new PreviewRenderUtility();
        readonly GameObject rig;
        readonly List<Mesh> bakedMeshes = new List<Mesh>();
        readonly List<DrawnRenderer> renderers = new List<DrawnRenderer>();
        readonly List<Material> materials = new List<Material>();
        readonly Dictionary<Material, Material> mutedMaterials = new Dictionary<Material, Material>();
        readonly HashSet<Material> excludedMaterials = new HashSet<Material>();
        readonly Dictionary<Material, Material> retainedMaterials = new Dictionary<Material, Material>();
        readonly float radius = .5f;
        Material candidate;
        Texture2D without;
        Texture2D with;

        internal IReadOnlyList<Material> Materials => materials;
        internal Texture2D Without => without;
        internal Texture2D With => with;
        internal bool HasGeometry => renderers.Count > 0;

        internal MatCapPreview(GameObject model)
        {
            rig = NewObject("MatCap preview");
            preview.AddSingleGO(rig);
            var content = NewObject("Model").transform;
            content.SetParent(rig.transform, false);
            var copies = new Dictionary<Transform, Transform> { { model.transform, content } };
            var seen = new HashSet<Material>();
            foreach (var source in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh;
                if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                else if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    bakedMeshes.Add(mesh);
                    skinned.BakeMesh(mesh);
                }
                else continue;
                var original = source.sharedMaterials;
                foreach (var material in original)
                    if (material != null && seen.Add(material)) materials.Add(material);
                if (mesh == null || !source.enabled || !IsVisible(source.transform, model.transform)) continue;
                var copy = CopyTransform(source.transform, copies).gameObject;
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = copy.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = original;
                renderer.shadowCastingMode = source.shadowCastingMode;
                renderer.receiveShadows = source.receiveShadows;
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder;
                renderer.renderingLayerMask = source.renderingLayerMask;
                renderers.Add(new DrawnRenderer
                {
                    renderer = renderer,
                    original = original,
                    withoutMatCap = new Material[original.Length],
                    withMatCap = new Material[original.Length]
                });
            }

            if (renderers.Count > 0)
            {
                var bounds = renderers[0].renderer.bounds;
                for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].renderer.bounds);
                content.localPosition = -bounds.center;
                radius = Mathf.Max(bounds.extents.magnitude, .001f);
            }
            preview.camera.orthographic = true;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.17f, .19f, .22f, 1);
            preview.camera.allowHDR = false;
            preview.camera.nearClipPlane = radius * .01f;
            preview.camera.farClipPlane = radius * 12;
            preview.camera.transform.SetPositionAndRotation(Vector3.back * (radius * 4), Quaternion.identity);
            preview.lights[1].enabled = false;
            preview.ambientColor = new Color(.36f, .36f, .36f);
        }

        static GameObject NewObject(string name) => new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

        static bool IsVisible(Transform node, Transform root)
        {
            for (; node != root; node = node.parent)
                if (!node.gameObject.activeSelf) return false;
            return true;
        }

        static Transform CopyTransform(Transform source, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out var existing)) return existing;
            var copy = NewObject(source.name).transform;
            copy.SetParent(CopyTransform(source.parent, copies), false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            copies.Add(source, copy);
            return copy;
        }

        internal void Setup(Material slot, Texture2D texture, float strength, float darkRetention, bool useOriginalMaterial)
        {
            Object.DestroyImmediate(candidate);
            candidate = null;
            if (MatCapMaterial.IsCompatible(slot))
                candidate = useOriginalMaterial ? new Material(slot) { hideFlags = HideFlags.HideAndDontSave } :
                    CreateCandidate(slot, texture, strength, darkRetention);
            foreach (var item in renderers)
                for (int i = 0; i < item.original.Length; i++)
                {
                    var original = item.original[i];
                    if (!MatCapMaterial.IsCompatible(original))
                    {
                        item.withoutMatCap[i] = item.withMatCap[i] = original;
                        continue;
                    }
                    item.withoutMatCap[i] = GetMutedMaterial(original);
                    item.withMatCap[i] = GetRightMaterial(original, slot, item.withoutMatCap[i]);
                }
        }

        Material GetRightMaterial(Material original, Material slot, Material muted)
        {
            if (!IsMatCapEnabled(original)) return muted;
            if (original == slot && candidate != null) return candidate;
            return retainedMaterials.TryGetValue(original, out var retained) ? retained : original;
        }

        Material GetMutedMaterial(Material basis)
        {
            if (mutedMaterials.TryGetValue(basis, out var muted)) return muted;
            muted = new Material(basis) { hideFlags = HideFlags.HideAndDontSave };
            muted.SetFloat(MatCapMaterial.Strength, 0);
            mutedMaterials.Add(basis, muted);
            return muted;
        }

        internal void InvalidateMutedMaterials() => DestroyMaterials(mutedMaterials);

        static void DestroyMaterials(Dictionary<Material, Material> copies)
        {
            foreach (var material in copies.Values) Object.DestroyImmediate(material);
            copies.Clear();
        }

        internal bool IsMatCapEnabled(Material slot) => !excludedMaterials.Contains(slot);

        internal void SetMatCapEnabled(Material slot, bool enabled)
        {
            if (enabled) excludedMaterials.Remove(slot);
            else excludedMaterials.Add(slot);
        }

        static Material CreateCandidate(Material material, Texture2D image, float strength, float darkRetention)
        {
            var visual = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
            MatCapMaterial.Apply(visual, image, strength, darkRetention);
            return visual;
        }

        internal void RetainMaterial(Material slot, Texture2D image, float strength, float darkRetention)
        {
            ReleaseMaterial(slot);
            retainedMaterials.Add(slot, CreateCandidate(slot, image, strength, darkRetention));
        }

        internal void ReleaseMaterial(Material slot)
        {
            if (!retainedMaterials.TryGetValue(slot, out var retained)) return;
            retainedMaterials.Remove(slot);
            Object.DestroyImmediate(retained);
        }

        internal void Render(int width, int height, Vector2 rotation, MatCapLightSnapshot light)
        {
            if (!HasGeometry) return;
            Object.DestroyImmediate(without);
            Object.DestroyImmediate(with);
            without = with = null;
            preview.camera.orthographicSize = radius * Mathf.Max(1, (float)height / width);
            rig.transform.rotation = Quaternion.Euler(rotation.x, rotation.y, 0);
            light.Apply(preview.lights[0]);
            without = RenderFrame(width, height, false);
            with = RenderFrame(width, height, true);
        }

        internal Texture2D RenderLightCardFlash(int width, int height, Texture2D texture)
        {
            var original = candidate.GetTexture(MatCapMaterial.Texture);
            candidate.SetTexture(MatCapMaterial.Texture, texture);
            try { return RenderFrame(width, height, true); }
            finally { candidate.SetTexture(MatCapMaterial.Texture, original); }
        }

        Texture2D RenderFrame(int width, int height, bool showMatCap)
        {
            foreach (var item in renderers) item.renderer.sharedMaterials = showMatCap ? item.withMatCap : item.withoutMatCap;
            preview.BeginStaticPreview(new Rect(0, 0, width, height));
            Texture2D result = null;
            try { preview.Render(true); }
            finally { result = preview.EndStaticPreview(); }
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }

        public void Dispose()
        {
            preview.Cleanup();
            InvalidateMutedMaterials();
            Object.DestroyImmediate(candidate);
            Object.DestroyImmediate(without);
            Object.DestroyImmediate(with);
            DestroyMaterials(retainedMaterials);
            foreach (var mesh in bakedMeshes) Object.DestroyImmediate(mesh);
        }
    }
}
