using July.Arch;

namespace July.Guide
{
    /// <summary>执行一个引导步骤；可包含一次或多次业务操作，不承担框架步骤游标的推进。
    /// 返回前释放本步骤持有的表现、订阅及输入许可，不按提示数量机械拆分步骤。</summary>
    public abstract class GuideStepProcedure : ProcedureBase
    {
        protected GuideStepContext Context { get; }
        protected GuideStepProcedure(GuideStepContext context) => Context = context;
    }
}
