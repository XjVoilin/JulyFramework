#if JULYGF_DEBUG
using System;
using System.Collections.Generic;

namespace July.Diagnostics
{
    public sealed class GMCategoryInfo
    {
        public string Category;
        public Type SourceType;
        public List<GMCommandInfo> Commands = new();
    }
}
#endif
