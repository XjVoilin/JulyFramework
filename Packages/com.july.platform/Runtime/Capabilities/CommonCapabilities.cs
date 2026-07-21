using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    public readonly struct PlatformLoginResult
    {
        public bool Succeeded { get; }
        public string Code { get; }
        public string Error { get; }

        public PlatformLoginResult(bool succeeded, string code, string error = null)
        {
            Succeeded = succeeded;
            Code = code;
            Error = error;
        }
    }

    public interface ILoginCapability : IPlatformCapability
    {
        UniTask<PlatformLoginResult> LoginAsync(CancellationToken cancellationToken);
    }

    public interface IShareCapability : IPlatformCapability
    {
        UniTask<bool> ShareAsync(string title, string imageUrl,
            IReadOnlyDictionary<string, string> extras, CancellationToken cancellationToken);
    }

    public interface IAdvertisementCapability : IPlatformCapability
    {
        UniTask<bool> ShowRewardedAsync(string placement, CancellationToken cancellationToken);
        UniTask<bool> ShowInterstitialAsync(string placement, CancellationToken cancellationToken);
    }
}
