using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
/// <summary>
/// Tip 项组件
/// 挂载在 Tip 预制体上
/// </summary>
public class UITipItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _text;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private RectTransform _rectTransform;

    private Action<UITipItem> _onComplete;
    private Tween _fadeTween;
    private Tweener _moveTweener;
    private float _duration;
    private float _fadeOutDuration;

    public string Message { get; private set; }

    private void Awake()
    {
        if (_rectTransform == null)
            _rectTransform = GetComponent<RectTransform>();

        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();

        if (_text == null)
            _text = GetComponentInChildren<TextMeshProUGUI>();
    }

    /// <summary>
    /// 显示 Tip
    /// </summary>
    /// <param name="message">提示内容</param>
    /// <param name="duration">显示时长</param>
    /// <param name="fadeOutDuration">淡出时长</param>
    /// <param name="onComplete">完成回调</param>
    /// <param name="enterOffset">入场偏移量（从下方滑入的距离）</param>
    /// <param name="enterDuration">入场动画时长</param>
    public void Show(string message, float duration, float fadeOutDuration, Action<UITipItem> onComplete, 
        float enterOffset = 0f, float enterDuration = 0.2f)
    {
        _onComplete = onComplete;
        _duration = duration;
        _fadeOutDuration = fadeOutDuration;
        Message = message;

        if (_text != null)
        {
            _text.text = message;
        }

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
        }

        gameObject.SetActive(true);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);

        KillTweens();

        if (_rectTransform != null && enterOffset > 0f)
        {
            _rectTransform.anchoredPosition = new Vector2(0, -enterOffset);
            _moveTweener = _rectTransform.DOAnchorPosY(0f, enterDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }
        else if (_rectTransform != null)
        {
            _rectTransform.anchoredPosition = Vector2.zero;
        }

        StartFadeTimer();
    }

    /// <summary>
    /// 重置计时器（同内容去重时调用）
    /// </summary>
    public void RestartTimer()
    {
        _fadeTween?.Kill();
        _fadeTween = null;

        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        if (_rectTransform != null)
            _rectTransform.DOPunchScale(Vector3.one * 0.1f, 0.15f, 1, 0f)
                .SetLink(gameObject);

        StartFadeTimer();
    }

    private void StartFadeTimer()
    {
        _fadeTween = DOVirtual.DelayedCall(_duration, () =>
        {
            FadeOut(_fadeOutDuration);
        }).SetLink(gameObject);
    }

    private void FadeOut(float duration)
    {
        if (_canvasGroup == null)
        {
            Complete();
            return;
        }

        _fadeTween = _canvasGroup.DOFade(0f, duration)
            .SetEase(Ease.OutQuad)
            .OnComplete(Complete)
            .SetLink(gameObject);
    }

    private void Complete()
    {
        _onComplete?.Invoke(this);
        _onComplete = null;
    }

    /// <summary>
    /// 向上移动
    /// </summary>
    /// <param name="offset">移动距离</param>
    /// <param name="duration">动画时长</param>
    public void MoveUp(float offset, float duration)
    {
        if (_rectTransform == null)
        {
            return;
        }

        _moveTweener?.Kill();
        var targetY = _rectTransform.anchoredPosition.y + offset;
        _moveTweener = _rectTransform.DOAnchorPosY(targetY, duration)
            .SetEase(Ease.OutQuad)
            .SetLink(gameObject);
    }

    /// <summary>
    /// 获取 Tip 高度
    /// </summary>
    public float GetHeight()
    {
        if (_rectTransform != null)
        {
            return _rectTransform.rect.height;
        }

        return 40f;
    }

    /// <summary>
    /// 重置状态
    /// </summary>
    public void Reset()
    {
        KillTweens();

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
        }

        if (_rectTransform != null)
        {
            _rectTransform.anchoredPosition = Vector2.zero;
        }

        if (_text != null)
        {
            _text.text = string.Empty;
        }

        Message = null;
        _onComplete = null;
    }

    private void KillTweens()
    {
        _fadeTween?.Kill();
        _moveTweener?.Kill();
        _fadeTween = null;
        _moveTweener = null;
    }

    private void OnDestroy()
    {
        KillTweens();
    }
}
}
