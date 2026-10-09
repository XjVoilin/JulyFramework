using UnityEngine;

namespace July.Input
{
    public enum PointerPhase { Began, Moved, Stationary, Ended, Canceled }

    /// <summary>玩法指针样本。ID 仅在本次按压期间标识同一指针；坐标和位移使用屏幕像素。</summary>
    public readonly struct PointerSample
    {
        public int Id { get; }
        public PointerPhase Phase { get; }
        public Vector2 Position { get; }
        public Vector2 Delta { get; }
        public Vector2 StartPosition { get; }
        public float StartTime { get; }
        public bool IsActive => Phase != PointerPhase.Ended && Phase != PointerPhase.Canceled;

        internal PointerSample(int id, PointerPhase phase, Vector2 position, Vector2 delta,
            Vector2 startPosition, float startTime)
        {
            Id = id;
            Phase = phase;
            Position = position;
            Delta = delta;
            StartPosition = startPosition;
            StartTime = startTime;
        }
    }

    /// <summary>
    /// 一次采样的不可变快照；终止样本也计入 PointerCount。Time 使用未缩放时间。
    /// ResetVersion 变化表示整个输入序列失效，即使当时没有按住的指针。
    /// </summary>
    public readonly struct InputFrame
    {
        private readonly PointerSample[] pointers;
        public int FrameCount { get; }
        public float Time { get; }
        public uint ResetVersion { get; }
        public int PointerCount { get; }

        internal InputFrame(int frameCount, float time, uint resetVersion, PointerSample[] pointers)
        {
            FrameCount = frameCount;
            Time = time;
            ResetVersion = resetVersion;
            this.pointers = pointers;
            PointerCount = pointers.Length;
        }

        /// <summary>按采样顺序读取；跨帧跟踪使用 Id，不使用集合索引。</summary>
        public PointerSample GetPointer(int index) => pointers[index];
    }

    public enum PointerCancellationReason { DeviceCanceled, DeviceLost, Blocked, FocusLost, Explicit, Shutdown }

    public readonly struct PointerCancellation
    {
        public int Id { get; }
        public Vector2 Position { get; }
        public PointerCancellationReason Reason { get; }

        internal PointerCancellation(int id, Vector2 position, PointerCancellationReason reason)
        {
            Id = id;
            Position = position;
            Reason = reason;
        }
    }
}
