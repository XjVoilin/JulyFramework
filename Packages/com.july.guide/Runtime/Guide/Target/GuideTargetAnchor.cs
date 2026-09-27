using System;
using July.Arch;
using UnityEngine;

namespace July.Guide
{
    public abstract class GuideTargetAnchor : GameView, IGuideTarget
    {
        [SerializeField] private int _targetId;
        private IGuideSystem _registeredSystem;

        public int TargetId => _targetId;
        public abstract Rect ScreenRect { get; }

        protected override void OnViewEnable()
        {
            if (_targetId <= 0)
                throw new InvalidOperationException($"Guide target on {name} requires a positive TargetId.");
            _registeredSystem = GetSystem<IGuideSystem>() ??
                throw new InvalidOperationException($"Guide target on {name} requires an initialized IGuideSystem.");
            _registeredSystem.RegisterTarget(this);
        }

        protected override void OnViewDisable()
        {
            // Scene objects may disable after ArchContext.Current has already been cleared.
            var registeredSystem = _registeredSystem;
            _registeredSystem = null;
            registeredSystem?.UnregisterTarget(this);
        }
    }
}
