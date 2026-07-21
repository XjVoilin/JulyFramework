using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Launch
{
    public interface ILaunchStep
    {
        string Name { get; }
        UniTask<bool> ExecuteAsync(CancellationToken ct);
    }
}
