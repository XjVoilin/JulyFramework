#if JULYGF_DEBUG
using System;

namespace July.Diagnostics
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class GMCommandAttribute : Attribute
    {
        public string DisplayName { get; }
        public int Order { get; set; }
        public bool CloseAfter { get; set; } = true;

        public GMCommandAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
#endif
