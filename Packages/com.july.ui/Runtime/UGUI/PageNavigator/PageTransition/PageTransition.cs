using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    public enum PageDirection
    {
        Forward = 1,
        Backward = -1
    }

    public struct PageTransitionContext
    {
        public RectTransform FromPage;
        public RectTransform ToPage;
        public Vector2 FromOriginalPosition;
        public Vector2 ToOriginalPosition;
        public PageDirection Direction;
        public float SlideDistance;
    }

    /// <summary>
    /// 翻页过渡效果基类
    /// </summary>
    public abstract class PageTransition : MonoBehaviour
    {
        /// <summary>
        /// 创建并返回过渡动画
        /// 实现时只需关注动画本身，不需要管页面显隐。
        /// </summary>
        public abstract Tween Play(PageTransitionContext ctx);
    }
}
