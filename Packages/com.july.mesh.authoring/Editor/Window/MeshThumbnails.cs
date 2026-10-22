using System;
using UnityEditor;
using UnityEngine;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    internal sealed class MeshThumbnails : IDisposable
    {
        readonly PreviewRenderUtility preview = new PreviewRenderUtility();
        readonly Material fallback;

        internal MeshThumbnails()
        {
            fallback = new Material(Shader.Find("Hidden/July/MeshAuthoringPreview")) { hideFlags = HideFlags.HideAndDontSave };
            preview.camera.fieldOfView = 30;
            preview.camera.clearFlags = CameraClearFlags.Color;
            preview.camera.backgroundColor = EditorGUIUtility.isProSkin ? new Color(.20f, .20f, .20f) : new Color(.72f, .72f, .72f);
            preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.Euler(40, 35, 0);
            preview.lights[1].intensity = .6f;
            preview.ambientColor = new Color(.45f, .45f, .45f);
        }

        internal Texture2D Render(UMesh mesh, Bounds originalBounds, int width = 200, int height = 130)
        {
            float radius = Mathf.Max(originalBounds.extents.magnitude, .001f);
            float distance = radius / Mathf.Sin(15 * Mathf.Deg2Rad) * 1.15f;
            preview.camera.transform.position = new Vector3(0, 0, -distance);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.nearClipPlane = Mathf.Max(.0001f, distance - radius * 2);
            preview.camera.farClipPlane = distance + radius * 3;
            preview.BeginStaticPreview(new Rect(0, 0, width, height));
            var matrix = Matrix4x4.Rotate(Quaternion.Euler(-12, 25, 0)) * Matrix4x4.Translate(-originalBounds.center);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                preview.DrawMesh(mesh, matrix, fallback, sub);
            preview.Render(true);
            return preview.EndStaticPreview();
        }

        public void Dispose() { preview.Cleanup(); UnityEngine.Object.DestroyImmediate(fallback); }
    }

}
