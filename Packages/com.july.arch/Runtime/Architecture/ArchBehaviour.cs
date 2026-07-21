using UnityEngine;

namespace July.Arch
{
    /// <summary>
    /// 框架 MonoBehaviour 基类 — 仅声明 Arch 能力接口，不占 Unity 生命周期。
    /// <para>子类通过 this.GetStore / this.Subscribe 等扩展方法访问框架能力。</para>
    /// <para>
    /// 两个直接子类各自管理生命周期：
    /// <list type="bullet">
    ///   <item><see cref="GameView"/> — 场景对象 / UI 子组件（Awake/OnDestroy/OnEnable/OnDisable + ISingletonView）</item>
    ///   <item>UIView（July.UI）— UISystem 管理的面板（仅 OnDisable 清理 + Open/Close）</item>
    /// </list>
    /// </para>
    /// </summary>
    public abstract class ArchBehaviour : MonoBehaviour,
        ICanGetStore, ICanGetSystem, ICanEvent, ICanGetView
    {
    }
}
