namespace July.Build
{
    public interface IBuildStep
    {
        string Name { get; }

        /// <summary>返回 null 或空字符串表示预检通过。</summary>
        string Validate(BuildContext context);

        BuildStepResult Execute(BuildContext context);
    }
}
