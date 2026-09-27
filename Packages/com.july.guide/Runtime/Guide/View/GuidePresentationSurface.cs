using System;
using UnityEngine;

namespace July.Guide
{
    internal sealed class GuidePresentationSurface : MonoBehaviour, ICanvasRaycastFilter
    {
        internal Func<Vector2, bool> BlocksRaycast;
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
            => BlocksRaycast == null || BlocksRaycast(screenPoint);
    }
}
