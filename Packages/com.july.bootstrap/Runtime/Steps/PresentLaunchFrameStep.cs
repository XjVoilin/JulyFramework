using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;

namespace July.Bootstrap
{
    internal sealed class PresentLaunchFrameStep : ILaunchStep
    {
        public string Name => "Present Launch Frame";
        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            await UniTask.NextFrame(ct);
            return true;
        }
    }
}