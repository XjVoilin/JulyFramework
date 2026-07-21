using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Platform
{
    public class DefaultLoginService : ILoginService
    {
        public string Code { get; private set; }

        public UniTask LoginAsync()
        {
            // editor登录不需要Code
            // Code = SystemInfo.deviceUniqueIdentifier;
            return UniTask.CompletedTask;
        }
    }
}

