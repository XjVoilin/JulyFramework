using System;
using UnityEngine;

namespace July.UI
{
    public enum UILayer
    {
        Background = 0,
        Normal = 100,
        Popup = 200,
        Loading = 300,
        Top = 400,
        Guide = 500
    }

    public enum UIAnimationType
    {
        None = 0,
        Animator = 1,
        Fade = 2,
        Scale = 3,
        SlideFromTop = 4,
        SlideFromBottom = 5,
        SlideFromLeft = 6,
        SlideFromRight = 7
    }

    /// <summary>
    /// 窗口串行调度模式。挂载于 <see cref="UIOpenOptions.QueueMode"/>。
    /// </summary>
    public enum UIQueueMode
    {
        /// <summary>普通窗口，立即打开，不受串行管控。</summary>
        None = 0,
        /// <summary>串行窗口，入队尾；当前串行窗口关闭后自动开下一个。</summary>
        Enqueue = 1,
        /// <summary>串行窗口，入队首（插队）；下一个就会打开它。</summary>
        EnqueueFirst = 2
    }

    [Serializable]
    public class WindowIdentifier : IEquatable<WindowIdentifier>
    {
        public int ID { get; }
        public string WindowName { get; }

        public WindowIdentifier(int id, string windowName)
        {
            ID = id;
            WindowName = windowName ?? throw new ArgumentNullException(nameof(windowName), "窗口名称不能为null");
        }

        public bool Equals(WindowIdentifier other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ID == other.ID && WindowName == other.WindowName;
        }

        public override bool Equals(object obj) => Equals(obj as WindowIdentifier);

        public override int GetHashCode()
        {
            unchecked
            {
                return (ID * 397) ^ (WindowName?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() => $"WindowIdentifier(ID={ID}, Name={WindowName})";

        public static bool operator ==(WindowIdentifier left, WindowIdentifier right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(WindowIdentifier left, WindowIdentifier right) => !(left == right);
    }

    [Serializable]
    public class UIOpenOptions
    {
        public WindowIdentifier WindowIdentifier { get; set; }
        public UILayer Layer { get; set; } = UILayer.Normal;
        public bool AddToStack { get; set; } = true;
        public UIQueueMode QueueMode { get; set; } = UIQueueMode.None;
        public object Data { get; set; } = null;
        public UIAnimationType OpenAnimationType { get; set; } = UIAnimationType.None;
        public UIAnimationType CloseAnimationType { get; set; } = UIAnimationType.None;
        public bool ShowMask { get; set; } = false;
        public bool ClickMaskToClose { get; set; } = false;
        public bool IgnoreSafeArea { get; set; } = false;
    }

    public interface IUIWindowProvider
    {
        bool TryResolve(int windowId, out UIOpenOptions options);
    }

    public class UIInfo
    {
        public UIView View { get; internal set; }
        public GameObject GameObject => View != null ? View.gameObject : null;
        public int WindowId { get; internal set; }
        public WindowIdentifier WindowIdentifier { get; internal set; }
        public UILayer Layer { get; internal set; }
        public bool IgnoreSafeArea { get; internal set; }
        public CanvasGroup CanvasGroup { get; internal set; }
        public UIAnimationType CloseAnimationType { get; internal set; }
        public UIQueueMode QueueMode { get; internal set; }
        public bool IsValid => View != null && View.IsOpened;

        public void Visible(bool isShow)
        {
            if (CanvasGroup != null)
            {
                CanvasGroup.alpha = isShow ? 1f : 0f;
                CanvasGroup.interactable = isShow;
                CanvasGroup.blocksRaycasts = isShow;
            }

            if (GameObject != null)
                GameObject.SetActive(isShow);
        }

        public void SetInteractable(bool interactable)
        {
            if (CanvasGroup != null)
            {
                CanvasGroup.interactable = interactable;
                CanvasGroup.blocksRaycasts = interactable;
            }
        }
    }
}
