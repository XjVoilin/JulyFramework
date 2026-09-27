using System;
using System.Collections.Generic;
using July.Arch;

namespace July.Guide
{
    public sealed class GuideStore : StoreBase<GuideStoreData>
    {
        private readonly HashSet<int> _completed = new();
        private readonly HashSet<int> _skipped = new();

        public bool IsCompleted(int guideId) => _completed.Contains(guideId);
        public bool IsSkipped(int guideId) => _skipped.Contains(guideId);
        public bool IsFinished(int guideId) => IsCompleted(guideId) || IsSkipped(guideId);

        protected override void OnDataReplaced()
        {
            _completed.Clear();
            _skipped.Clear();
            Restore(Data.CompletedGuideIds, _completed);
            Restore(Data.SkippedGuideIds, _skipped);
            if (_completed.Overlaps(_skipped))
                throw new InvalidOperationException("A saved guide cannot be both completed and skipped.");
        }

        internal void Commit(int guideId, GuideExitReason reason)
        {
            if (IsFinished(guideId)) throw new InvalidOperationException($"Guide {guideId} already has a durable outcome.");
            if (reason == GuideExitReason.Completed)
            {
                _completed.Add(guideId);
                Data.CompletedGuideIds.Add(guideId);
            }
            else if (reason == GuideExitReason.Skipped)
            {
                _skipped.Add(guideId);
                Data.SkippedGuideIds.Add(guideId);
            }
            else throw new ArgumentException("Only completion and explicit skip are durable.", nameof(reason));
            MarkDirty();
        }

        /// <summary>Explicit replay/debug action. Stop the current guide before resetting its record.</summary>
        public void Reset(int guideId)
        {
            var changed = _completed.Remove(guideId) | _skipped.Remove(guideId);
            if (!changed) return;
            Data.CompletedGuideIds.Remove(guideId);
            Data.SkippedGuideIds.Remove(guideId);
            MarkDirty();
        }

        private static void Restore(List<int> source, HashSet<int> destination)
        {
            if (source == null) throw new InvalidOperationException("Guide saves must contain outcome lists.");
            foreach (var id in source)
                if (id <= 0 || !destination.Add(id))
                    throw new InvalidOperationException($"Invalid or duplicate saved guide id {id}.");
        }
    }
}
