namespace July.Input
{
    internal sealed class ClickRecognizer : IGestureRecognizer
    {
        private readonly float toleranceSquared;
        private bool withinTolerance;

        internal ClickRecognizer(float tolerance) => toleranceSquared = tolerance * tolerance;

        public void Begin(GestureSample sample) => withinTolerance = true;

        public GestureResult Update(GestureSample sample)
        {
            withinTolerance &= sample.Displacement.sqrMagnitude <= toleranceSquared;
            return default;
        }

        public GestureResult End(GestureSample sample)
        {
            Update(sample);
            var result = withinTolerance ? new GestureResult(GestureKind.Click, sample.ScreenPosition) : default;
            withinTolerance = false;
            return result;
        }

        public void Cancel() => withinTolerance = false;
    }
}
