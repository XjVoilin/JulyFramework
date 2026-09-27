using July.Arch;

namespace July.Guide
{
    /// <summary>One complete teaching unit. Release owned presentation/subscriptions before returning.</summary>
    public abstract class GuideStepProcedure : ProcedureBase
    {
        protected GuideStepContext Context { get; }
        protected GuideStepProcedure(GuideStepContext context) => Context = context;
    }
}
