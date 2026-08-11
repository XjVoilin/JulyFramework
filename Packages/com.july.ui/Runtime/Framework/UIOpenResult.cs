using System;

namespace July.UI
{
    public enum UIOpenError
    {
        None = 0,
        InvalidOptions,
        Cancelled,
        OpenFailed
    }

    public readonly struct UIOpenResult
    {
        private UIOpenResult(UIView view, UIOpenError error, string message, Exception exception)
        {
            View = view;
            Error = error;
            Message = message;
            Exception = exception;
        }

        public bool IsSuccess => Error == UIOpenError.None;
        public bool IsFailure => !IsSuccess;
        public UIView View { get; }
        public UIOpenError Error { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public static UIOpenResult Success(UIView view)
            => new(view, UIOpenError.None, null, null);

        public static UIOpenResult Failure(UIOpenError error, string message, Exception exception = null)
            => new(null, error, message, exception);

        public override string ToString()
            => IsSuccess ? $"Success: {View}" : $"Failure({Error}): {Message}";
    }
}
