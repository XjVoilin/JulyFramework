using UnityEngine;

namespace July.Rendering.URP
{
    [RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    public sealed class MeshHighlightCamera : MonoBehaviour
    {
        public MeshHighlightTarget Target { get; set; }
        void OnDisable() => Target = null;
    }
}
