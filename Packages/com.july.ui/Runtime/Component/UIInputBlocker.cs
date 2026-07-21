using July.Arch;

namespace July.UI
{
    public interface IInputGate
    {
        void Block();
        void Unblock();
    }

    /// <summary>
    /// 挂在需要屏蔽游戏输入的 UI 面板上（如 Help 弹窗、暂停菜单、全屏遮罩等）。
    /// 面板 SetActive(true) 时自动屏蔽，SetActive(false) 时自动恢复。
    /// 支持嵌套：多个 Blocker 同时激活时，全部关闭后才恢复输入。
    /// </summary>
    public class UIInputBlocker : ArchBehaviour
    {
        private void OnEnable() => ArchContext.Current?.TryGetSystem<IInputGate>()?.Block();
        private void OnDisable() => ArchContext.Current?.TryGetSystem<IInputGate>()?.Unblock();
    }
}
