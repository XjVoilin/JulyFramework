using System;

namespace July.Build
{
    public enum BuildOutcome { Succeeded, Failed, Cancelled }

    public sealed class BuildResult
    {
        public BuildOutcome Outcome { get; }
        public bool Succeeded => Outcome == BuildOutcome.Succeeded;
        public string FailedStep { get; }
        public string Error { get; }
        public TimeSpan Elapsed { get; }

        internal BuildResult(BuildOutcome outcome, string failedStep, string error, TimeSpan elapsed)
        {
            Outcome = outcome;
            FailedStep = failedStep;
            Error = error;
            Elapsed = elapsed;
        }
    }
}
