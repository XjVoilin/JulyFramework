using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    internal enum ColliderDisplay { Overlay, ShapesOnly, Xray }
    // One renderer shared by visible rows. No item behaviour or Rigidbody is instantiated.
    internal sealed class ColliderPreview : IDisposable
    {
        readonly PreviewRenderUtility renderer = new PreviewRenderUtility();
        readonly Material solid, wire, depth, fill, edge;

        internal sealed class Model
        {
            internal Mesh[] Meshes;
            internal Matrix4x4[] Matrices;
            internal Bounds Bounds;

            internal static Model Read(ColliderRow row)
            {
                var layout = row.Inspection.Layout;
                var item = layout.ItemRoot;
                var sources = layout.Sources;
                if (sources.Count == 0) throw new InvalidOperationException("模型没有静态 Mesh。");
                var matrices = sources.Select(s => item.worldToLocalMatrix * s.Transform.localToWorldMatrix).ToArray();
                var points = sources.SelectMany((s, i) =>
                {
                    var bounds = s.Mesh.bounds;
                    return Enumerable.Range(0, 8).Select(corner => matrices[i].MultiplyPoint3x4(bounds.center +
                        Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                            (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                }).ToArray();
                var result = new Model { Meshes = sources.Select(s => s.Mesh).ToArray(), Matrices = matrices,
                    Bounds = new Bounds(points[0], Vector3.zero) };
                foreach (var point in points) result.Bounds.Encapsulate(point);
                return result;
            }
        }

        internal ColliderPreview()
        {
            var shader = Shader.Find("Hidden/July/ColliderOverviewPreview");
            solid = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            wire = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            depth = LayerMaterial(shader, 1900, CompareFunction.LessEqual, true, 0, false);
            fill = LayerMaterial(shader, 2000, CompareFunction.Equal, false, 15, true);
            edge = LayerMaterial(shader, 2100, CompareFunction.LessEqual, false, 15, false);
            solid.SetFloat("_OffsetFactor", -1); solid.SetFloat("_OffsetUnits", -1);
            wire.SetFloat("_OffsetFactor", -1); wire.SetFloat("_OffsetUnits", -1);
            edge.SetFloat("_OffsetFactor", -1); edge.SetFloat("_OffsetUnits", -1);
            wire.SetFloat("_ZTest", (float)CompareFunction.Always);
            wire.SetFloat("_Shading", 0);
            renderer.camera.clearFlags = CameraClearFlags.Color;
            renderer.camera.orthographic = true;
            renderer.camera.transform.rotation = Quaternion.identity;
        }

        static Material LayerMaterial(Shader shader, int queue, CompareFunction compare, bool write, int mask, bool shading)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            material.SetFloat("_ZTest", (float)compare); material.SetFloat("_ZWrite", write ? 1 : 0);
            material.SetFloat("_ColorMask", mask); material.SetFloat("_Shading", shading ? 1 : 0);
            return material;
        }

        internal Texture2D Render(Model model, ColliderRow row, ColliderPreviewShapes shapes, int column,
            ColliderDisplay display, int width, int height, Bounds bounds)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, .001f);
            var camera = renderer.camera;
            var background = EditorGUIUtility.isProSkin ? new Color(.16f, .16f, .16f) : new Color(.77f, .77f, .77f);
            camera.backgroundColor = background;
            camera.orthographicSize = radius * 1.12f;
            if (width < height) camera.orthographicSize *= (float)height / width;
            camera.transform.position = Vector3.back * radius * 4;
            camera.nearClipPlane = radius * .01f;
            camera.farClipPlane = radius * 8;
            var rotation = Quaternion.Euler(row.Rotation.x, row.Rotation.y, 0);
            var pose = Matrix4x4.Rotate(rotation) * Matrix4x4.Translate(-bounds.center);
            if (column == 0) return Frame(width, height, false, () => DrawModel(new Color(.76f, .76f, .76f, 1)));
            var color = column == 1 ? new Color(.95f, .63f, .25f, 1) : new Color(.32f, .85f, .50f, 1);
            if (display == ColliderDisplay.Xray)
                return Frame(width, height, false, () =>
                {
                    DrawModel(new Color(.48f, .48f, .48f, 1));
                    wire.SetColor("_Color", color); renderer.DrawMesh(shapes.Wire, pose, wire, 0);
                });

            Texture2D reference = null, surface = null, outlines = null;
            try
            {
                bool overlay = display == ColliderDisplay.Overlay;
                if (overlay) reference = Frame(width, height, false, () => DrawModel(new Color(.76f, .76f, .76f, 1)));
                camera.backgroundColor = Color.clear;
                fill.SetColor("_Color", color); edge.SetColor("_Color", color);
                surface = Frame(width, height, true, () =>
                {
                    renderer.DrawMesh(shapes.Surface, pose, depth, 0);
                    renderer.DrawMesh(shapes.Surface, pose, fill, 0);
                });
                var lines = shapes.Contours(rotation);
                outlines = Frame(width, height, true, () =>
                {
                    renderer.DrawMesh(shapes.Surface, pose, depth, 0);
                    renderer.DrawMesh(lines, pose, edge, 0);
                });
                var modelPixels = reference != null ? reference.GetPixels() : null;
                var pixels = surface.GetPixels(); var edges = outlines.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = overlay ? Color.Lerp(background, modelPixels[i], .38f) : background;
                    pixel = Color.Lerp(pixel, pixels[i], pixels[i].a * (overlay ? .28f : .78f));
                    pixel = Color.Lerp(pixel, edges[i], edges[i].a * .96f); pixel.a = 1; pixels[i] = pixel;
                }
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels(pixels); result.Apply(); return result;
            }
            finally { Object.DestroyImmediate(reference); Object.DestroyImmediate(surface); Object.DestroyImmediate(outlines); }

            void DrawModel(Color gray)
            {
                solid.SetColor("_Color", gray);
                for (int i = 0; i < model.Meshes.Length; i++)
                    for (int sub = 0; sub < model.Meshes[i].subMeshCount; sub++)
                        renderer.DrawMesh(model.Meshes[i], pose * model.Matrices[i], solid, sub);
            }
        }

        Texture2D Frame(int width, int height, bool rgba, Action draw)
        {
            renderer.BeginStaticPreview(new Rect(0, 0, width, height));
            bool rendered = false;
            try
            {
                draw();
                renderer.Render(true);
                rendered = true;
                if (rgba)
                {
                    // EndStaticPreview discards alpha. Capture the native RGBA target first.
                    var active = RenderTexture.active;
                    var result = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        RenderTexture.active = renderer.camera.targetTexture;
                        result.ReadPixels(new Rect(0, 0, width, height), 0, 0); result.Apply();
                        return result;
                    }
                    catch { Object.DestroyImmediate(result); throw; }
                    finally { RenderTexture.active = active; }
                }
            }
            finally
            {
                // RGBA captures own their pixels; the generated opaque thumbnail is unnecessary.
                if (rgba || !rendered) Object.DestroyImmediate(renderer.EndStaticPreview());
            }
            var image = renderer.EndStaticPreview();
            image.hideFlags = HideFlags.HideAndDontSave;
            return image;
        }

        public void Dispose()
        {
            renderer.Cleanup();
            Object.DestroyImmediate(solid);
            Object.DestroyImmediate(wire);
            Object.DestroyImmediate(depth); Object.DestroyImmediate(fill); Object.DestroyImmediate(edge);
        }
    }
}
