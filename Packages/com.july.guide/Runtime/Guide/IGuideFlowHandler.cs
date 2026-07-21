using System;

namespace July.Guide
{
    /// <summary>
    /// Per-flow trigger interface. Each flow has one handler that listens for
    /// trigger conditions and advances step completion.
    /// </summary>
    public interface IGuideFlowHandler : IDisposable
    {
        string FlowId { get; }

        /// <summary>Called on registration to subscribe trigger conditions.</summary>
        void OnRegister();

        /// <summary>Called when the flow starts to subscribe step events.</summary>
        void OnFlowStart();
    }
}
