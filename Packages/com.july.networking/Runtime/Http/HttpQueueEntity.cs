using System;
using Cysharp.Threading.Tasks;

namespace July.Networking
{
    public abstract class HttpQueueEntity : HttpEntityBase
    {
        public uint RequestId { get; private set; } = GenerateRequestId();

        public void RegenerateRequestId() => RequestId = GenerateRequestId();

        private static uint _nextId = unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        private static uint GenerateRequestId() => ++_nextId;

        public virtual bool IsOptimistic => true;

        public virtual void ApplyLocal() { }

        protected internal virtual void OnResponse() { }
        protected internal virtual void OnError() { }

        private UniTaskCompletionSource<bool> _tcs;

        public UniTask<bool> Completion => (_tcs ??= new UniTaskCompletionSource<bool>()).Task;

        internal void SetCompleted()
        {
            _tcs?.TrySetResult(IsOk);
            _tcs = null;
        }
    }

    public abstract class HttpQueueEntity<TResp> : HttpQueueEntity
    {
        public TResp RespData { get; protected set; }
    }

    public abstract class HttpQueueEntity<TReq, TResp> : HttpQueueEntity<TResp>
    {
        public abstract TReq RqtData { get; }
    }
}
