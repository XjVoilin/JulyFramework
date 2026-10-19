using UnityEngine;

namespace July.Physics.Authoring
{
    /// <summary>标记碰撞体制作的输出父节点，不参与运行时逻辑。</summary>
    [DisallowMultipleComponent]
    public sealed class ColliderAuthoringTarget : MonoBehaviour
    {
    }
}
