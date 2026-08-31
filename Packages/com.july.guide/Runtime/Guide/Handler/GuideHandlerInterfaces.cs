using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Guide
{
    public interface IGuideConditionHandler
    {
        int Type { get; }
        bool Evaluate(int paramId);
    }

    public interface IGuideActionHandler
    {
        int Type { get; }
        UniTask ExecuteAsync(int paramId, CancellationToken cancellationToken);
    }

    public interface IGuideCompletionHandler
    {
        int Type { get; }
        UniTask WaitAsync(GuideCompletionContext context, int paramId, CancellationToken cancellationToken);
    }

    public interface IGuideViewHandler : IDisposable
    {
        int Type { get; }
        UniTask OpenAsync(GuideViewContext context, GuideViewData data, CancellationToken cancellationToken);
        UniTask CloseAsync(CancellationToken cancellationToken);
    }
}
