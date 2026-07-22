namespace July.Build
{
    public readonly struct BuildStepResult
    {
        public bool Succeeded { get; }
        public string Error { get; }

        private BuildStepResult(bool succeeded, string error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        public static BuildStepResult Success() => new(true, null);

        public static BuildStepResult Failure(string error) =>
            new(false, string.IsNullOrWhiteSpace(error) ? "Build step failed." : error);
    }
}
