using Cysharp.Threading.Tasks;

namespace July.UI
{
    /// <summary>
    /// 场景过渡动画接口。
    /// 由过渡窗口实现，供 SceneTransitionHandler 驱动入场/退场动画。
    /// </summary>
    public interface ISceneTransitionView
    {
        UniTask PlayEnterAsync(object options = null);
        UniTask PlayExitAsync();
    }
}
