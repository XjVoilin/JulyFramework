using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Platform
{
    public class DefaultLoginService : ILoginService
    {
        public string Code { get; private set; }

        public UniTask LoginAsync(System.Threading.CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // editor登录不需要Code
            // 设备标识示例：Code = SystemInfo.deviceUniqueIdentifier;
            return UniTask.CompletedTask;
        }
    }
}

