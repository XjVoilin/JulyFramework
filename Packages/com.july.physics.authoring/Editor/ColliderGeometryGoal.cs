using UnityEngine;

namespace July.Physics.Authoring
{
    public enum ColliderGeometryGoal
    {
        [InspectorName("整体包围")]
        Envelope,
        [InspectorName("结构贴合")]
        Structure
    }
}
