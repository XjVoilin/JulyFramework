#if JULYGF_DEBUG
using System.Reflection;

namespace July.Diagnostics
{
    public sealed class GMCommandInfo
    {
        public string DisplayName;
        public string Group;
        public int Order;
        public bool CloseAfter;
        public MethodInfo Method;
        public GMParamInfo[] Params;

        public object Invoke(object[] args) => Method.Invoke(null, args);
    }
}
#endif
