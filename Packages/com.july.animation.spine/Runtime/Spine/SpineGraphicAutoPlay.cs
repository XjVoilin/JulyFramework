using Spine.Unity;
using UnityEngine;
using AnimationState = Spine.AnimationState;

namespace July.Animation.Spine
{
    [RequireComponent(typeof(SkeletonGraphic))]
    public class SpineGraphicAutoPlay : SpineAutoPlayBase
    {
        private SkeletonGraphic _skeletonGraphic;

        private void Awake()
        {
            _skeletonGraphic = GetComponent<SkeletonGraphic>();
        }

        protected override AnimationState GetAnimationState()
        {
            if (_skeletonGraphic == null) return null;
            var animStateComponent = _skeletonGraphic.GetComponent<IAnimationStateComponent>();
            return animStateComponent != null ? animStateComponent.AnimationState : null;
        }
    }
}
