using System;
using July.Arch;
using July.Launch;
using UnityEngine;

namespace July.Bootstrap
{
    /// <summary>管理标准应用的 Arch 生命周期，不依赖启动流水线是否已经完成。</summary>
    public abstract class BootstrapGameEntry : JulyGameEntry
    {
        private ArchContext _architecture;

        protected override void OnBeforeLaunch()
        {
            if (ArchContext.Current != null)
                throw new InvalidOperationException("Another application architecture is already active.");
            _architecture = new ArchContext();
        }

        private void Update() => _architecture?.Update(Time.deltaTime);

        protected override void OnShutdown()
        {
            _architecture?.Shutdown();
            _architecture = null;
        }
    }
}
