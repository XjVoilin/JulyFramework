using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIEmptyGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
