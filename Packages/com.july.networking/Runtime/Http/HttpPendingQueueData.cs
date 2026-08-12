using System;
using System.Collections.Generic;
using July.Arch;

namespace July.Networking
{
    /// <summary>
    /// HTTP 待重放请求的状态所有者。是否参与本地持久化由项目组合根决定。
    /// </summary>
    public sealed class HttpPendingQueueStore : StoreBase<HttpPendingQueueData>
    {
        public bool HasEntries => Data.Entries.Count > 0;

        internal void Enqueue(string path, string body)
        {
            Data.Entries.Add(new HttpPendingEntry
            {
                Path = path,
                Body = body
            });
            MarkDirty();
        }

        internal bool TryPeek(out HttpPendingEntry entry)
        {
            if (Data.Entries.Count == 0)
            {
                entry = null;
                return false;
            }

            entry = Data.Entries[0];
            return true;
        }

        internal void Dequeue()
        {
            if (Data.Entries.Count == 0) return;
            Data.Entries.RemoveAt(0);
            MarkDirty();
        }

        protected override void OnDataReplaced()
        {
            Data.Entries ??= new List<HttpPendingEntry>();
        }
    }

    [Serializable]
    public class HttpPendingQueueData
    {
        public List<HttpPendingEntry> Entries = new();
    }

    [Serializable]
    public class HttpPendingEntry
    {
        public string Path;
        public string Body;
    }
}
