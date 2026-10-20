using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Rendering.URP
{
    [DisallowMultipleComponent]
    public sealed class MeshHighlightTarget : MonoBehaviour
    {
        [SerializeField] MeshRenderer[] _renderers;
        [SerializeField] Material _material;
        Mesh[] _meshes;
        bool _highlighted;

        public bool IsHighlighted => _highlighted && isActiveAndEnabled;
        internal Material Material => _material;

        void Awake()
        {
            if (_material == null || _material.shader.name != "July/URP/Mesh Highlight")
                throw new InvalidOperationException(name + ": configure the mask highlight material.");
            if (_renderers == null || _renderers.Length == 0)
                throw new InvalidOperationException(name + ": configure the static MeshRenderers.");
            _meshes = new Mesh[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _meshes[i] = _renderers[i].GetComponent<MeshFilter>().sharedMesh;
        }

        public void SetHighlighted(bool value) => _highlighted = value;
        void OnDisable() => _highlighted = false;

        internal bool GetScreenRect(Camera camera, int width, int height, out Rect rect)
        {
            // Orthographic projection is affine. Project each renderer's AABB directly,
            // so an off-screen/disconnected node cannot inflate the group's scissor.
            var view = camera.worldToCameraMatrix;
            var projection = camera.projectionMatrix * view;
            float margin = (_material.GetFloat("_OutlineWidth") + _material.GetFloat("_GlowWidth") * 1.25f) *
                (height / camera.orthographicSize + width) * .01f + 2;
            Vector2 min = new Vector2(width, height), max = Vector2.zero;
            bool found = false;
            foreach (var renderer in _renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var bounds = renderer.bounds;
                var center = projection.MultiplyPoint3x4(bounds.center);
                var extent = bounds.extents;
                float depth = -view.MultiplyPoint3x4(bounds.center).z;
                float depthExtent = Mathf.Abs(view.m20) * extent.x + Mathf.Abs(view.m21) * extent.y + Mathf.Abs(view.m22) * extent.z;
                if (depth + depthExtent < camera.nearClipPlane || depth - depthExtent > camera.farClipPlane) continue;
                float xExtent = Mathf.Abs(projection.m00) * extent.x + Mathf.Abs(projection.m01) * extent.y + Mathf.Abs(projection.m02) * extent.z;
                float yExtent = Mathf.Abs(projection.m10) * extent.x + Mathf.Abs(projection.m11) * extent.y + Mathf.Abs(projection.m12) * extent.z;
                var lower = new Vector2((center.x - xExtent + 1) * .5f * width - margin,
                    (center.y - yExtent + 1) * .5f * height - margin);
                var upper = new Vector2((center.x + xExtent + 1) * .5f * width + margin,
                    (center.y + yExtent + 1) * .5f * height + margin);
                if (upper.x <= 0 || upper.y <= 0 || lower.x >= width || lower.y >= height) continue;
                min = Vector2.Min(min, lower); max = Vector2.Max(max, upper); found = true;
            }
            rect = found ? Rect.MinMaxRect(Mathf.Max(0, Mathf.Floor(min.x)), Mathf.Max(0, Mathf.Floor(min.y)),
                Mathf.Min(width, Mathf.Ceil(max.x)), Mathf.Min(height, Mathf.Ceil(max.y))) : default;
            return found && rect.width > 0 && rect.height > 0;
        }

        internal void RecordMask(CommandBuffer commands, int pass)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                for (int subMesh = 0; subMesh < _meshes[i].subMeshCount; subMesh++)
                    commands.DrawMesh(_meshes[i], renderer.localToWorldMatrix, _material, subMesh, pass);
            }
        }
    }
}
