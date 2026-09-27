using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using July.UI;

namespace July.Guide
{
    /// <summary>
    /// Owns guide interaction; July UI owns the window and its resources. CloseAsync runs on the main thread.
    /// All calls and cancellation sources follow the GuideSystem Unity-main-thread contract.
    /// </summary>
    public sealed class GuidePresentation
    {
        private readonly IUISystem _ui;
        private readonly int _windowId;
        private GuideWindow _window;
        private GuideUguiSkin _skin;
        private GuidePresentationSurface _surface;
        private Operation _operation;

        public GuidePresentation(IUISystem ui, int windowId)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _windowId = windowId;
        }

        public async UniTask OpenAsync(GuideStepContext context, GuideViewData data, int raycastMode,
            int targetId, CancellationToken cancellationToken)
        {
            Validate(data, raycastMode, targetId);
            var operation = new Operation(context, data, raycastMode, targetId, cancellationToken);
            var previous = _operation;
            // Publish replacement state before cancellation resumes the old operation's awaiters.
            _operation = operation;
            Hide(previous);
            try
            {
                if (previous != null)
                    Release(previous);
                operation.Token.ThrowIfCancellationRequested();
                if (targetId != 0)
                    operation.Target = await context.WaitForTargetAsync(targetId, operation.Token);

                operation.Token.ThrowIfCancellationRequested();
                if (_window == null)
                {
                    var opened = await _ui.OpenAsync(_windowId, this, operation.Token);
                    if (opened == null || !ReferenceEquals(opened, _window))
                        throw new InvalidOperationException($"Guide window {_windowId} must be a GuideWindow exclusively owned by this presentation.");
                }
                else
                    Configure(operation);
                operation.Token.ThrowIfCancellationRequested();
                Refresh(operation);
            }
            catch
            {
                await CloseAsync();
                throw;
            }
        }

        // Called by the real UIView before opening, so configuration precedes animation and interaction.
        internal void AttachWindow(GuideWindow window)
        {
            if (_window != null)
                throw new InvalidOperationException("A guide presentation already owns a window.");
            _window = window;
            _skin = window.Skin;
            if (_skin == null || _skin.gameObject == window.gameObject || _skin.RaycastSurface == null)
                throw new InvalidOperationException("GuideWindow requires a child skin with a full-screen RaycastSurface.");
            _surface = _skin.RaycastSurface.gameObject.AddComponent<GuidePresentationSurface>();
            window.Closing = () =>
            {
                var operation = _operation;
                if (operation != null && !operation.Token.IsCancellationRequested)
                    Fail(operation, new InvalidOperationException($"Guide window {_windowId} was closed before its interaction finished."));
            };
            Configure(_operation);
        }

        private void Configure(Operation operation)
        {
            operation.Skin = _skin;
            operation.ConfirmHandler = () => Confirm(operation);
            operation.SkipHandler = () => Skip(operation);
            _skin.Confirmed += operation.ConfirmHandler;
            _skin.SkipRequested += operation.SkipHandler;
            _skin.Configure(operation.Data,
                string.IsNullOrEmpty(operation.Data.TextKey) ? string.Empty : operation.Context.ResolveText(operation.Data.TextKey),
                operation.Context.CanSkip ? operation.Context.ResolveText(operation.Data.SkipTextKey) : string.Empty,
                operation.Context.CanSkip);
            _skin.ShowConfirmation(false, true);
            _skin.RaycastSurface.raycastTarget = operation.RaycastMode != GuideRaycastModes.AllowAll;
            _window.Refresh = () => Refresh(operation);
            _surface.BlocksRaycast = point => BlocksRaycast(operation, point);
            _skin.gameObject.SetActive(true);
        }

        public UniTask WaitForConfirmationAsync(CancellationToken cancellationToken)
        {
            var operation = _operation ?? throw new InvalidOperationException("Open the guide presentation first.");
            operation.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            operation.Context.SetWaitingFor("Confirmation");
            operation.Skin.ShowConfirmation(true, true, operation.Context.ResolveText(operation.Data.ConfirmTextKey));
            return operation.Confirmation.Task.AttachExternalCancellation(operation.Token)
                .AttachExternalCancellation(cancellationToken);
        }

        /// <summary>Await this in the owning Procedure's finally, without its cancelled execution token.</summary>
        public async UniTask CloseAsync()
        {
            var operation = _operation;
            _operation = null;
            Hide(operation, false);
            var window = _window;
            if (window != null) window.Closing = null;
            _window = null;
            _skin = null;
            _surface = null;
            try
            {
                if (operation != null) Release(operation);
            }
            finally
            {
                if (window != null) await _ui.CloseAsync(window, CancellationToken.None);
            }
        }

        private void Hide(Operation operation, bool hideContent = true)
        {
            if (operation?.Skin != null)
            {
                operation.Skin.Confirmed -= operation.ConfirmHandler;
                operation.Skin.SkipRequested -= operation.SkipHandler;
            }
            if (_window != null) _window.Refresh = null;
            if (_surface != null) _surface.BlocksRaycast = null;
            if (hideContent && _skin != null) _skin.gameObject.SetActive(false);
        }

        private static void Release(Operation operation)
        {
            try
            {
                operation.Cancellation.Cancel();
            }
            finally
            {
                operation.Confirmation.TrySetCanceled(operation.Token);
                operation.Cancellation.Dispose();
            }
        }

        private void Confirm(Operation operation)
        {
            if (!ReferenceEquals(_operation, operation) || operation.Token.IsCancellationRequested)
                return;
            try
            {
                ReadTargetRect(operation);
                operation.Skin.ShowConfirmation(true, false);
            }
            catch (Exception exception)
            {
                Fail(operation, exception);
                return;
            }
            operation.Confirmation.TrySetResult();
        }

        private void Skip(Operation operation)
        {
            if (ReferenceEquals(_operation, operation) && !operation.Token.IsCancellationRequested)
                operation.Context.RequestSkip();
        }

        private void Refresh(Operation operation)
        {
            if (!ReferenceEquals(_operation, operation))
                return;
            if (operation.Token.IsCancellationRequested)
            {
                Hide(operation);
                return;
            }
            try
            {
                operation.Skin.SetTargetRect(ReadTargetRect(operation));
            }
            catch (Exception exception)
            {
                // Live scene geometry and project-authored skin code are observation boundaries.
                Fail(operation, exception);
            }
        }

        private bool BlocksRaycast(Operation operation, Vector2 point)
        {
            if (!ReferenceEquals(_operation, operation) || operation.Token.IsCancellationRequested)
                return true;
            try
            {
                var rect = ReadTargetRect(operation);
                return operation.RaycastMode == GuideRaycastModes.BlockAll ||
                    operation.RaycastMode == GuideRaycastModes.BlockOutsideTarget && !rect.Value.Contains(point);
            }
            catch (Exception exception)
            {
                Fail(operation, exception);
                return true;
            }
        }

        private void Fail(Operation operation, Exception exception)
        {
            if (!ReferenceEquals(_operation, operation))
                return;
            try
            {
                operation.Context.Fail(exception);
            }
            finally
            {
                Hide(operation);
            }
        }

        private static Rect? ReadTargetRect(Operation operation)
        {
            if (operation.TargetId == 0)
                return null;
            if (!operation.Context.IsTargetRegistered(operation.Target))
                throw new InvalidOperationException($"Guide target {operation.TargetId} disappeared while its presentation was open.");
            return operation.Target.ScreenRect;
        }

        private static void Validate(GuideViewData data, int raycastMode, int targetId)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (raycastMode < GuideRaycastModes.BlockAll || raycastMode > GuideRaycastModes.AllowAll)
                throw new ArgumentOutOfRangeException(nameof(raycastMode));
            if (targetId < 0 || raycastMode == GuideRaycastModes.BlockOutsideTarget && targetId == 0)
                throw new ArgumentException("A target raycast hole requires a positive TargetId.", nameof(targetId));
        }

        private sealed class Operation
        {
            internal readonly GuideStepContext Context;
            internal readonly GuideViewData Data;
            internal readonly int RaycastMode;
            internal readonly int TargetId;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken Token;
            internal readonly UniTaskCompletionSource Confirmation = new();
            internal IGuideTarget Target;
            internal GuideUguiSkin Skin;
            internal Action ConfirmHandler;
            internal Action SkipHandler;

            internal Operation(GuideStepContext context, GuideViewData data, int raycastMode,
                int targetId, CancellationToken cancellationToken)
            {
                Context = context;
                Data = data;
                RaycastMode = raycastMode;
                TargetId = targetId;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RunToken, cancellationToken);
                Token = Cancellation.Token;
            }
        }
    }
}
