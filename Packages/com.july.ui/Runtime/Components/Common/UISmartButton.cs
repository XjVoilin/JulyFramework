using DG.Tweening;
using July.Arch;
using July.Audio;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace July.UI
{
[DisallowMultipleComponent]
public class UISmartButton : ArchBehaviour,
    IPointerClickHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerExitHandler
{
    [FormerlySerializedAs("interactable")]
    [Header("Interact")] [SerializeField] private bool _interactable = true;

    [Header("Scale Feedback")] public bool enableScale = true;
    public float scaleTarget = 1.11f;
    public float scaleTime = 0.1f;
    public Ease scaleEase = Ease.Linear;

    [Header("Click Cooldown")] public bool enableCooldown = true;
    public float cooldownTime = 0.5f;

    [Header("Audio")]
    public bool enableSound = true;
    [Tooltip("点击音效名（留空使用 AudioSystem 配置的默认值）")]
    [FormerlySerializedAs("clickSfx")]
    [SerializeField] private string _clickSfx;

    [Header("Event")] public UnityEvent onClick = new();

    private Vector3 _originScale;
    private Vector3 _pressedScale;
    private float _lastClickTime;

    private void Awake()
    {
        _originScale = transform.localScale;
        _pressedScale = _originScale * scaleTarget;
        _targetScale = _originScale;
    }

    private void OnDestroy()
    {
        _scaleTweener?.Kill();
    }

    #region Pointer

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!_interactable || !enableScale)
            return;

        _scaleTweener?.Kill();
        _scaleTweener = transform.DOScale(_pressedScale, scaleTime)
            .SetEase(scaleEase)
            .SetUpdate(true)
            .SetLink(gameObject);
        _targetScale = _pressedScale;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_interactable || !enableScale)
            return;

        // 抬起时用动画恢复
        PlayScaleRestore();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_interactable || !enableScale)
            return;

        // 离开时用动画恢复
        PlayScaleRestore();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_interactable)
            return;

        if (!TryProcessClick())
            return;

        PlayClickSound();
        onClick?.Invoke();
    }

    #endregion

    #region Public API

    public bool IsInteractable => _interactable;

    public virtual void SetInteractable(bool value)
    {
        if (_interactable == value)
            return;

        _interactable = value;

        if (!_interactable)
        {
            _scaleTweener?.Kill();
            transform.localScale = _originScale;
        }
    }

    #endregion

    #region Internal

    private Tweener _scaleTweener;
    private Vector3 _targetScale;

    private void PlayScaleRestore()
    {
        // 如果已经是原始大小，跳过
        if (_targetScale == _originScale)
            return;

        _targetScale = _originScale;

        // Kill 当前动画
        _scaleTweener?.Kill();

        // 从当前位置平滑恢复到原始大小
        _scaleTweener = transform.DOScale(_originScale, scaleTime)
            .SetEase(scaleEase)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private bool TryProcessClick()
    {
        if (!enableCooldown)
            return true;

        var now = Time.unscaledTime;
        if (now - _lastClickTime < cooldownTime)
            return false;

        _lastClickTime = now;
        return true;
    }

    private void PlayClickSound()
    {
        if (!enableSound) return;
        this.GetSystem<IAudioSystem>()?.PlayClickSfx(_clickSfx);
    }

    #endregion
}
}
