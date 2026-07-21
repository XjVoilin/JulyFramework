using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.UI
{
    internal sealed class AnimatorAnimationStrategy : IUIAnimationStrategy
    {
        public static readonly AnimatorAnimationStrategy Instance = new();

        public async UniTask PlayAsync(GameObject target, bool isOpen, CancellationToken ct = default)
        {
            if (target == null) return;

            var animator = target.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            var stateName = isOpen
                ? UIAnimationConstants.AnimatorOpenState
                : UIAnimationConstants.AnimatorCloseState;

            animator.Play(stateName, 0, 0f);
            animator.Update(0f);

            await WaitForAnimatorState(animator, stateName, ct);
        }

        private static async UniTask WaitForAnimatorState(Animator animator, string stateName, CancellationToken ct)
        {
            var stateHash = Animator.StringToHash(stateName);

            while (!ct.IsCancellationRequested && animator != null)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.shortNameHash == stateHash)
                    break;
                await UniTask.Yield(ct);
            }

            while (!ct.IsCancellationRequested && animator != null)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.shortNameHash != stateHash ||
                    stateInfo.normalizedTime >= UIAnimationConstants.AnimatorCompleteThreshold)
                    break;
                await UniTask.Yield(ct);
            }
        }
    }
}
