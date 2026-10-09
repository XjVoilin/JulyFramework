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
        /// <summary>读取当前帧，不消费输入、不派发业务回调。屏蔽或取消后应重新读取。</summary>
        InputFrame ReadFrame();
        /// <summary>查询屏幕位置是否命中 UGUI，不改变已有指针的归属。</summary>
        bool IsOverUI(Vector2 screenPosition);
        /// <summary>按 ID 撤销未完成的交互；设备取消在正常更新通知，显式取消/屏蔽/失焦立即通知。</summary>
        event Action<PointerCancellation> PointerCanceled;
        event Action<Vector2> Clicked;
        event Action<InputDirection> Direction;
        /// <summary>撤销所有未完成的玩法指针，递增 ResetVersion；等待释放后接受新按压，不改变阻断计数。</summary>
        void CancelPointer();
    }
}
