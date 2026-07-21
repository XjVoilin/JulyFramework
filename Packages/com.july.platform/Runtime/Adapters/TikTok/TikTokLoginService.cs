#if JULYGF_DY_MINIGAME
using System;
using Cysharp.Threading.Tasks;
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public class TikTokLoginService : ILoginService
    {
        public string Code { get; private set; }

        public UniTask LoginAsync()
        {
            var tcs = new UniTaskCompletionSource();
            TT.Login(
                (code, anonymousCode, isLogin) =>
                {
                    Code = code;
                    tcs.TrySetResult();
                },
                error =>
                {
                    Debug.LogError($"TT.Login failed: {error}");
                    tcs.TrySetException(new Exception($"TT.Login failed: {error}"));
                });
            return tcs.Task;
        }
    }
}
#endif

