using July.Arch;
using UnityEngine;

namespace July.Guide
{
    public abstract class GuideTargetAnchor : GameView, IGuideTarget
    {
        [SerializeField] private int _targetId;

        public int TargetId => _targetId;
        public abstract Rect ScreenRect { get; }

        protected override void OnViewEnable()
        {
            if (_targetId <= 0)
                throw new System.InvalidOperationException($"Guide target on {name} requires a positive TargetId.");
            GetSystem<IGuideSystem>().RegisterTarget(this);
        }

        protected override void OnViewDisable()
        {
            GetSystem<IGuideSystem>().UnregisterTarget(this);
        }
    }
}
