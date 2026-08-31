using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Guide
{
    internal sealed class EmptyGuideViewHandler : IGuideViewHandler
    {
        public int Type => GuideBuiltInTypes.NoView;
        public UniTask OpenAsync(GuideViewContext context, GuideViewData data, CancellationToken cancellationToken)
            => UniTask.CompletedTask;
        public UniTask CloseAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
        public void Dispose() { }
    }
}
