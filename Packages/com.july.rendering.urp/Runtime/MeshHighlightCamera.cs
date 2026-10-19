using UnityEngine;

namespace July.Rendering.URP
{
    /// <summary>The current highlight candidate for one orthographic scene camera.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class MeshHighlightCamera : MonoBehaviour
    {
        public MeshHighlightTarget Target { get; set; }

        private void OnDisable() => Target = null;
    }
}
