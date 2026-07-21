#if JULYGF_DEBUG
using System;

namespace July.Diagnostics
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public class GMParamAttribute : Attribute
    {
        public string DisplayName { get; }

        public GMParamAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
#endif
