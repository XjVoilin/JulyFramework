#if JULYGF_WX_MINIGAME
using System;
using Cysharp.Threading.Tasks;
using WeChatWASM;
using UnityEngine;

namespace July.Platform
{
    public class WeChatLoginService : ILoginService
    {
        public string Code { get; private set; }

        public UniTask LoginAsync()
        {
            var tcs = new UniTaskCompletionSource();
            WX.Login(new LoginOption
            {
                success = res =>
                {
                    Code = res.code;
                    tcs.TrySetResult();
                },
                fail = res =>
                {
                    Debug.LogError($"WX.Login failed: {res.errMsg}");
                    tcs.TrySetException(new Exception($"WX.Login failed: {res.errMsg}"));
                },
            });
            return tcs.Task;
        }
    }
}
#endif

