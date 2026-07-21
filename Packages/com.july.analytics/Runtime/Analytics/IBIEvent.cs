using System.Collections.Generic;

namespace July.Analytics
{
    public interface IBIEvent
    {
        string EventName { get; }
        Dictionary<string, object> ToParams();
    }

    public interface IBIProperties
    {
        Dictionary<string, object> ToParams();
    }
}
