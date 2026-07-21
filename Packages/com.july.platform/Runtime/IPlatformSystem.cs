namespace July.Platform
{
    /// <summary>
    /// 平台系统接口 — 统一平台类型判断、服务访问、设备交互。
    /// 通过 Scope.GetSystem&lt;IPlatformSystem&gt;() 获取。
    /// </summary>
    public interface IPlatformSystem
    {
        int PlatformType { get; }
        T GetService<T>() where T : class;
        void DeferAllServices();
        void VibrateShort(VibrateType type = VibrateType.Light);
        void VibrateLong();
    }
}
