#if JULYGF_DY_MINIGAME
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public class TikTokLoginService : ILoginService
    {
        public string Code { get; private set; }

        public async UniTask LoginAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var tcs = new UniTaskCompletionSource<string>();
            using var cancellation = ct.Register(() => tcs.TrySetCanceled(ct));
            TT.Login(
                (code, anonymousCode, isLogin) =>
                {
                    tcs.TrySetResult(code);
                },
                error =>
                {
                    Debug.LogError($"TT.Login failed: {error}");
                    tcs.TrySetException(new Exception($"TT.Login failed: {error}"));
                });
            var loginCode = await tcs.Task;
            ct.ThrowIfCancellationRequested();
            Code = loginCode;
        }
    }
}
#endif
