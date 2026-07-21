#if JULYGF_DEBUG
using System;
using TMPro;

namespace July.Diagnostics
{
    public interface IGMSystem
    {
        void Register(Type type);
        void Build(TMP_FontAsset font = null);
    }
}
#endif
