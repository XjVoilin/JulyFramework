using System;
using UnityEngine;

namespace July.Input
{
    public enum InputDirection { Up, Down, Left, Right }

    [Flags]
    public enum InputScope { Gameplay = 1, UI = 2, All = Gameplay | UI }

    /// <summary>统一阻断 July 玩法输入和接入的 UI；不阻断直接业务调用。</summary>
    public interface IInputGate
    {
        /// <summary>有效阻断范围变化后通知；订阅者读取当前状态。</summary>
        event Action BlockStateChanged;
        /// <summary>所选范围中，任一范围被阻断即为 true。</summary>
        bool IsBlocked(InputScope scope);
        /// <summary>调用方持有并释放凭据；多次申请互不覆盖。</summary>
        IDisposable Block(InputScope scope);
    }

    public interface IInputSystem : IInputGate
    {
        event Action<Vector2> Clicked;
        event Action<InputDirection> Direction;
        /// <summary>撤销未完成的玩法指针交互，不改变阻断状态。</summary>
        void CancelPointer();
    }
}
