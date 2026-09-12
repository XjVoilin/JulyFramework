#if JULYGF_WX_MINIGAME
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using WeChatWASM;
using UnityEngine;

namespace July.Platform
{
    public class WeChatLoginService : ILoginService
    {
        public string Code { get; private set; }

        public async UniTask LoginAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var tcs = new UniTaskCompletionSource<string>();
            using var cancellation = ct.Register(() => tcs.TrySetCanceled(ct));
            WX.Login(new LoginOption
            {
                success = res =>
                {
                    tcs.TrySetResult(res.code);
                },
                fail = res =>
                {
                    Debug.LogError($"WX.Login failed: {res.errMsg}");
                    tcs.TrySetException(new Exception($"WX.Login failed: {res.errMsg}"));
                },
            });
            var code = await tcs.Task;
            ct.ThrowIfCancellationRequested();
            Code = code;
        }
    }
}
#endif

