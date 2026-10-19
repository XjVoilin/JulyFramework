using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Rendering.URP
{
    /// <summary>A preconfigured static-mesh object, sharing its normal geometry and highlight material.</summary>
    [DisallowMultipleComponent]
    public sealed class MeshHighlightTarget : MonoBehaviour
    {
        private readonly struct Surface
        {
            internal readonly MeshRenderer Renderer;
            internal readonly Mesh Mesh;
            internal readonly int SubMeshCount;

            internal Surface(MeshRenderer renderer)
            {
                Renderer = renderer;
                Mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                SubMeshCount = Mesh.subMeshCount;
            }
        }

        [SerializeField] private MeshRenderer[] _renderers;
        [SerializeField] private Material _material;
        private Surface[] _surfaces;
        private MaterialPropertyBlock _properties;
        private bool _highlighted;
        private readonly Matrix4x4[] _directionMatrices = new Matrix4x4[4];
        private readonly Vector4[] _directions = new Vector4[4];
        private static readonly int DirectionId = Shader.PropertyToID("_HighlightDirection");

        public bool IsHighlighted => _highlighted && isActiveAndEnabled;

        private void Awake()
        {
            // Serialized package inputs form the configuration boundary. Invalid assets must not silently skip draws.
            if (_material == null || _material.shader.name != "July/URP/Mesh Highlight" || !_material.enableInstancing)
                throw new InvalidOperationException($"{name}: assign a July/URP/Mesh Highlight material with GPU Instancing enabled.");
            if (_renderers == null || _renderers.Length == 0)
                throw new InvalidOperationException($"{name}: configure at least one static MeshRenderer for highlighting.");
            _surfaces = new Surface[_renderers.Length];
            for (var i = 0; i < _renderers.Length; i++) _surfaces[i] = new Surface(_renderers[i]);
            _properties = new MaterialPropertyBlock();
        }

        public void SetHighlighted(bool highlighted) => _highlighted = highlighted;

        private void OnDisable() => _highlighted = false;

        internal void RecordHighlight(CommandBuffer commands, float aspect)
        {
            // All surfaces share one stencil silhouette: finish each stage before advancing to the next.
            DrawDirections(commands, 0, aspect);
            DrawSubMeshes(commands, 1);
            DrawSubMeshes(commands, 2);
            DrawDirections(commands, 3, aspect);
            DrawDirections(commands, 4, aspect);
            DrawSubMeshes(commands, 5);
            DrawSubMeshes(commands, 6);
            DrawSubMeshes(commands, 7);
        }

        private void DrawDirections(CommandBuffer commands, int pass, float aspect)
        {
            const float diagonal = 0.70711f;
            for (var i = 0; i < 4; i++)
                _directions[i] = new Vector4(i == 0 || i == 3 ? -diagonal : diagonal,
                    (i < 2 ? diagonal : -diagonal) * aspect, 0f, 0f);
            _properties.SetVectorArray(DirectionId, _directions);
            foreach (var surface in _surfaces)
            {
                if (!surface.Renderer.enabled || !surface.Renderer.gameObject.activeInHierarchy) continue;
                var matrix = surface.Renderer.transform.localToWorldMatrix;
                for (var i = 0; i < 4; i++) _directionMatrices[i] = matrix;
                for (var subMesh = 0; subMesh < surface.SubMeshCount; subMesh++)
                    commands.DrawMeshInstanced(surface.Mesh, subMesh, _material, pass,
                        _directionMatrices, 4, _properties);
            }
        }

        private void DrawSubMeshes(CommandBuffer commands, int pass)
        {
            foreach (var surface in _surfaces)
            {
                if (!surface.Renderer.enabled || !surface.Renderer.gameObject.activeInHierarchy) continue;
                for (var subMesh = 0; subMesh < surface.SubMeshCount; subMesh++)
                    commands.DrawMesh(surface.Mesh, surface.Renderer.transform.localToWorldMatrix,
                        _material, subMesh, pass, _properties);
            }
        }
    }
}
