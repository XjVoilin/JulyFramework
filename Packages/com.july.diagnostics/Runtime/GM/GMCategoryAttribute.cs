#if JULYGF_DEBUG
using System;

namespace July.Diagnostics
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class GMCategoryAttribute : Attribute
    {
        public string Category { get; }

        public GMCategoryAttribute(string category)
        {
            Category = category;
        }
    }
}
#endif
