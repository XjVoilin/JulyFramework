using System;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 通用三态奖励领取组件。
    /// 三种互斥状态：未完成（纯表现）、可领取（领取按钮）、已领取（纯表现）。
    /// 由父级 View 通过 Bind/SetState 驱动，自身不持有业务逻辑。
    /// </summary>
    public class UIAwardClaimItem : MonoBehaviour
    {
        [SerializeField] private GameObject _incompleteRoot;
        [SerializeField] private UISmartButton _claimBtn;
        [SerializeField] private GameObject _claimedRoot;

        private Action _onClaim;

        public void Bind(RewardActionState state, Action onClaim)
        {
            Unbind();
            _onClaim = onClaim;
            _claimBtn.onClick.AddListener(HandleClaim);
            SetState(state);
        }

        public void SetState(RewardActionState state)
        {
            _incompleteRoot.SetActive(state == RewardActionState.Incomplete);
            _claimBtn.gameObject.SetActive(state == RewardActionState.Claimable);
            _claimedRoot.SetActive(state == RewardActionState.Claimed);
        }

        public void Unbind()
        {
            _claimBtn.onClick.RemoveListener(HandleClaim);
            _onClaim = null;
        }

        private void HandleClaim() => _onClaim?.Invoke();
    }
}
