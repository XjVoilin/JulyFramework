namespace July.Build
{
    public interface IBuildStep
    {
        string Name { get; }

        /// <summary>Returns null or an empty string when validation succeeds.</summary>
        string Validate(BuildContext context);

        BuildStepResult Execute(BuildContext context);
    }
}
