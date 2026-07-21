using System;
using System.Collections.Generic;
using July.Persistence;

namespace July.Networking
{
    [Serializable]
    public class HttpPendingQueueData : ISaveData
    {
        public SaveImportance Importance => SaveImportance.Critical;
        public List<HttpPendingEntry> Entries = new();
    }

    [Serializable]
    public class HttpPendingEntry
    {
        public string Path;
        public string Body;
    }
}
