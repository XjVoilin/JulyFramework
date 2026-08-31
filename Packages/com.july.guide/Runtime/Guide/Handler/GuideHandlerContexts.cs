using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Guide
{
    public readonly struct GuideCompletionContext
    {
        public readonly int GuideId;
        public readonly int StepId;
        public readonly int TargetId;

        internal GuideCompletionContext(int guideId, int stepId, int targetId)
        {
            GuideId = guideId;
            StepId = stepId;
            TargetId = targetId;
        }
    }

    public readonly struct GuideViewContext
    {
        private readonly System.Func<UniTask<bool>> _skip;

        public readonly int GuideId;
        public readonly int StepId;
        public readonly bool CanSkip;
        public readonly int InputMode;
        public readonly int TargetId;
        public readonly Rect TargetRect;

        internal GuideViewContext(int guideId, int stepId, bool canSkip, int inputMode,
            int targetId, Rect targetRect, System.Func<UniTask<bool>> skip)
        {
            GuideId = guideId;
            StepId = stepId;
            CanSkip = canSkip;
            InputMode = inputMode;
            TargetId = targetId;
            TargetRect = targetRect;
            _skip = skip;
        }

        public UniTask<bool> SkipAsync() => _skip();
    }
}
