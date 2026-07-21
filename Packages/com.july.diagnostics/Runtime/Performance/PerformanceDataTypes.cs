using System;
using System.Collections.Generic;

namespace July.Diagnostics
{
    public class MemorySnapshot
    {
        public float TotalMemoryMB { get; set; }
        public float AllocatedMemoryMB { get; set; }
        public float ReservedMemoryMB { get; set; }
        public float MonoHeapSizeMB { get; set; }
        public float MonoUsedSizeMB { get; set; }
        public float GCTotalAllocatedMB { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class PerformanceSample
    {
        public string Name { get; set; }
        public int CallCount { get; set; }
        public float TotalTime { get; set; }
        public float AverageTime { get; set; }
        public float MinTime { get; set; }
        public float MaxTime { get; set; }
    }

    public class ResourceStatistics
    {
        public int TextureCount { get; set; }
        public float TextureMemoryMB { get; set; }
        public int AudioClipCount { get; set; }
        public float AudioClipMemoryMB { get; set; }
        public int MeshCount { get; set; }
        public float MeshMemoryMB { get; set; }
        public int MaterialCount { get; set; }
        public int GameObjectCount { get; set; }
    }

    public class GCStatistics
    {
        public float TotalAllocatedMB { get; set; }
        public float MonoHeapSizeMB { get; set; }
        public float MonoUsedSizeMB { get; set; }
        public int GCCount { get; set; }
        public float LastGCTime { get; set; }
        public float GCFrequency { get; set; }
    }

    public class PerformanceDataSnapshot
    {
        public DateTime Timestamp { get; set; }
        public float CurrentFPS { get; set; }
        public float AverageFPS { get; set; }
        public float MinFPS { get; set; }
        public float MaxFPS { get; set; }
        public MemorySnapshot Memory { get; set; }
        public GCStatistics GC { get; set; }
        public ResourceStatistics Resources { get; set; }
    }
}
