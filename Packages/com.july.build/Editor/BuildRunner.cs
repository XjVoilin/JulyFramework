using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace July.Build
{
    public sealed class BuildRunner
    {
        private readonly IBuildHost _host;

        public BuildRunner(IBuildHost host) =>
            _host = host ?? throw new ArgumentNullException(nameof(host));

        public BuildResult Run(BuildContext context, IEnumerable<IBuildStep> steps)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (steps == null) throw new ArgumentNullException(nameof(steps));

            var pipeline = steps.ToArray();
            if (pipeline.Any(step => step == null))
                throw new ArgumentException("构建步骤不能包含 null。", nameof(steps));

            var watch = Stopwatch.StartNew();
            string activeStep = null;
            try
            {
                foreach (var step in pipeline)
                {
                    var error = step.Validate(context);
                    if (!string.IsNullOrWhiteSpace(error))
                        return Finish(BuildOutcome.Failed, step.Name, error, watch);
                }

                if (context.Interactive && !_host.Confirm(context, pipeline.Length))
                    return Finish(BuildOutcome.Cancelled, null, null, watch);

                _host.SaveAssets();
                for (var i = 0; i < pipeline.Length; i++)
                {
                    var step = pipeline[i];
                    activeStep = step.Name;
                    _host.ShowProgress(step.Name, i + 1, pipeline.Length);
                    _host.Log($"[Build] [{i + 1}/{pipeline.Length}] {step.Name}");
                    var result = step.Execute(context);
                    if (!result.Succeeded)
                        return Finish(BuildOutcome.Failed, step.Name, result.Error, watch);
                }

                return Finish(BuildOutcome.Succeeded, null, null, watch);
            }
            catch (Exception exception)
            {
                _host.LogError(exception.ToString());
                return Finish(BuildOutcome.Failed, activeStep, exception.Message, watch);
            }
            finally
            {
                _host.ClearProgress();
            }
        }

        private static BuildResult Finish(BuildOutcome outcome, string step, string error,
            Stopwatch watch)
        {
            watch.Stop();
            return new BuildResult(outcome, step, error, watch.Elapsed);
        }
    }
}
