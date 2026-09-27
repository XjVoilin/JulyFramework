using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.UI;

namespace July.Guide
{
    /// <summary>Waits for an accepted target activation. This does not stand in for a business result.</summary>
    public sealed class GuideClickTargetProcedure : GuideStepProcedure
    {
        private readonly int _targetId;
        private readonly GuideViewData _view;
        private readonly int _raycastMode;
        private readonly int _windowId;

        public GuideClickTargetProcedure(GuideStepContext context, int windowId, int targetId, GuideViewData view,
            int raycastMode = GuideRaycastModes.BlockOutsideTarget) : base(context)
        {
            if (targetId <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetId));
            if (raycastMode == GuideRaycastModes.BlockAll)
                throw new ArgumentException("Clicking a target requires allowing its UGUI raycasts.", nameof(raycastMode));
            _targetId = targetId;
            _view = view;
            _raycastMode = raycastMode;
            _windowId = windowId;
        }

        protected override async UniTask OnExecuteAsync(CancellationToken cancellationToken)
        {
            var target = await Context.WaitForTargetAsync(_targetId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (target is not IGuideClickTarget clickTarget)
                throw new InvalidOperationException($"Guide target {_targetId} does not report accepted activations.");
            var clicked = new UniTaskCompletionSource();
            void OnClicked() => clicked.TrySetResult();
            var presentation = new GuidePresentation(GetSystem<IUISystem>(), _windowId);
            // Subscribe before displaying or releasing the player's next interaction.
            clickTarget.Clicked += OnClicked;
            try
            {
                await presentation.OpenAsync(Context, _view, _raycastMode, _targetId, cancellationToken);
                Context.SetWaitingFor($"Click target {_targetId}");
                await clicked.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                clickTarget.Clicked -= OnClicked;
                await presentation.CloseAsync();
            }
        }
    }
}
