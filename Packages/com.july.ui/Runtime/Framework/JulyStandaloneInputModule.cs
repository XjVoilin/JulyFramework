using System.Collections.Generic;
using July.Input;
using UnityEngine;
using UnityEngine.EventSystems;

namespace July.UI
{
    /// <summary>正常事件交给 UGUI，只负责统一阻断、交互取消和恢复。</summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class JulyStandaloneInputModule : StandaloneInputModule
    {
        private IInputGate gate;
        private bool uiBlocked;
        private bool dispatching;
        private bool cancelPending;
        private bool waitingForRelease;
        private bool clearSelectionAtEndOfFrame;
        private int resumeAfterFrame = -1;
        private readonly List<PointerEventData> cancelledPointers = new();

        internal void Bind(IInputGate inputGate)
        {
            gate = inputGate;
            gate.BlockStateChanged += OnBlockStateChanged;
            OnBlockStateChanged();
        }

        internal void Unbind()
        {
            gate.BlockStateChanged -= OnBlockStateChanged;
            gate = null;
            RequestCancellation();
        }

        protected override void OnDestroy()
        {
            // Unity 先销毁组件时仍需解除托管事件，不再向已销毁的 UI 派发取消事件。
            if (gate != null)
            {
                gate.BlockStateChanged -= OnBlockStateChanged;
                gate = null;
            }
            base.OnDestroy();
        }

        private void OnBlockStateChanged()
        {
            var blocked = gate.IsBlocked(InputScope.UI);
            if (blocked == uiBlocked) return;
            uiBlocked = blocked;
            resumeAfterFrame = Time.frameCount;
            if (blocked) RequestCancellation();
        }

        private void RequestCancellation()
        {
            waitingForRelease = true;
            clearSelectionAtEndOfFrame = true;
            resumeAfterFrame = Time.frameCount;
            cancelPending = true;
            if (!dispatching) CancelInteractions();
        }

        public override void UpdateModule()
        {
            // 基类失焦时走正常松开路径，可能发送 Drop；这里改为取消。
            if (!eventSystem.isFocused)
            {
                if (!waitingForRelease) RequestCancellation();
                return;
            }
            base.UpdateModule();
        }

        public override bool ShouldActivateModule() => true;

        public override void ActivateModule()
        {
            if (!uiBlocked && !waitingForRelease) base.ActivateModule();
        }

        public override void DeactivateModule()
        {
            RequestCancellation();
            base.DeactivateModule();
        }

        public override void Process()
        {
            if (cancelPending) CancelInteractions();
            if (uiBlocked || !eventSystem.isFocused || Time.frameCount <= resumeAfterFrame) return;
            if (waitingForRelease)
            {
                // 等到设备回到空闲再恢复；本帧的 Up/Down 不进入新的交互。
                if (!HasHeldInput()) waitingForRelease = false;
                return;
            }

            dispatching = true;
            try
            {
                base.Process();
            }
            finally
            {
                dispatching = false;
                // 回调内申请阻断时，允许当前这一轮 UGUI 处理结束后统一收尾。
                if (cancelPending) CancelInteractions();
            }
        }

        private bool HasHeldInput()
            => input.touchCount > 0 || input.GetMouseButton(0) || input.GetMouseButton(1) ||
               input.GetMouseButton(2) || input.GetAxisRaw(horizontalAxis) != 0f ||
               input.GetAxisRaw(verticalAxis) != 0f || UnityEngine.Input.GetButton(submitButton) ||
               UnityEngine.Input.GetButton(cancelButton);

        private void LateUpdate()
        {
            // 输入框可能在 LateUpdate 执行本轮已排定的激活，因此帧末再清除编辑焦点。
            if (!clearSelectionAtEndOfFrame && !uiBlocked) return;
            clearSelectionAtEndOfFrame = false;
            eventSystem.SetSelectedGameObject(null, GetBaseEventData());
        }

        private void CancelInteractions()
        {
            cancelPending = false;
            dispatching = true;
            try
            {
                cancelledPointers.AddRange(m_PointerData.Values);
                m_PointerData.Clear();
                foreach (var pointer in cancelledPointers)
                {
                    var press = pointer.pointerPress;
                    var drag = pointer.pointerDrag;
                    var wasDragging = pointer.dragging;
                    pointer.eligibleForClick = false;
                    pointer.pointerPress = null;
                    pointer.rawPointerPress = null;
                    pointer.pointerClick = null;
                    pointer.pointerDrag = null;
                    pointer.dragging = false;
                    ExecuteEvents.Execute(press, pointer, ExecuteEvents.pointerUpHandler);
                    if (wasDragging) ExecuteEvents.Execute(drag, pointer, ExecuteEvents.endDragHandler);
                    HandlePointerExitAndEnter(pointer, null);
                }
                eventSystem.SetSelectedGameObject(null, GetBaseEventData());
            }
            finally
            {
                cancelledPointers.Clear();
                dispatching = false;
            }
        }
    }
}
