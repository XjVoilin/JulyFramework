using System;
using UnityEngine;

namespace July.UI
{
    public enum RewardActionState
    {
        Incomplete,
        Claimable,
        Claimed
    }

    /// <summary>
    /// 通用三态奖励行组件。
    /// 三种互斥状态：未完成（去做）/ 可领取（领奖）/ 已领取（完成标记）。
    /// 由父级 View 通过 Bind/SetState 驱动，自身不持有业务逻辑。
    /// </summary>
    public class UIAwardActionItem : MonoBehaviour
    {
        [SerializeField] private UISmartButton _actionBtn;
        [SerializeField] private UISmartButton _claimBtn;
        [SerializeField] private GameObject _claimedRoot;

        private Action _onAction;
        private Action _onClaim;

        public void Bind(RewardActionState state, Action onAction, Action onClaim)
        {
            Unbind();
            _onAction = onAction;
            _onClaim = onClaim;
            _actionBtn.onClick.AddListener(HandleAction);
            _claimBtn.onClick.AddListener(HandleClaim);
            SetState(state);
        }

        public void SetState(RewardActionState state)
        {
            _actionBtn.gameObject.SetActive(state == RewardActionState.Incomplete);
            _claimBtn.gameObject.SetActive(state == RewardActionState.Claimable);
            _claimedRoot.SetActive(state == RewardActionState.Claimed);
        }

        public void Unbind()
        {
            _actionBtn.onClick.RemoveAllListeners();
            _claimBtn.onClick.RemoveAllListeners();
            _onAction = null;
            _onClaim = null;
        }

        private void HandleAction() => _onAction?.Invoke();
        private void HandleClaim() => _onClaim?.Invoke();
    }
}
