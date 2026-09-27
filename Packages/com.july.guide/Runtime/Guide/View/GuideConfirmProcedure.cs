using System.Threading;
using Cysharp.Threading.Tasks;
using July.UI;

namespace July.Guide
{
    /// <summary>Reusable confirmation interaction; the project supplies its presentation content.</summary>
    public sealed class GuideConfirmProcedure : GuideStepProcedure
    {
        private readonly GuideViewData _view;
        private readonly int _targetId;
        private readonly int _raycastMode;
        private readonly int _windowId;

        public GuideConfirmProcedure(GuideStepContext context, int windowId, GuideViewData view, int targetId = 0,
            int raycastMode = GuideRaycastModes.BlockAll) : base(context)
        {
            _view = view;
            _targetId = targetId;
            _raycastMode = raycastMode;
            _windowId = windowId;
        }

        protected override async UniTask OnExecuteAsync(CancellationToken cancellationToken)
        {
            var presentation = new GuidePresentation(GetSystem<IUISystem>(), _windowId);
            try
            {
                await presentation.OpenAsync(Context, _view, _raycastMode, _targetId, cancellationToken);
                await presentation.WaitForConfirmationAsync(cancellationToken);
            }
            finally { await presentation.CloseAsync(); }
        }
    }
}
