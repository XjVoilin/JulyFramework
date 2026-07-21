using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Diagnostics
{
    public interface IPerformanceSystem
    {
        #region FPS

        float CurrentFPS { get; }
        float AverageFPS { get; }
        float MinFPS { get; }
        float MaxFPS { get; }
        List<float> GetFPSHistory(int count = 60);

        #endregion

        #region Memory

        float TotalMemoryMB { get; }
        float AllocatedMemoryMB { get; }
        float ReservedMemoryMB { get; }
        float MonoHeapSizeMB { get; }
        float MonoUsedSizeMB { get; }
        float GCTotalAllocatedMB { get; }
        MemorySnapshot GetMemorySnapshot();
        GCStatistics GetGCStatistics();
        void ForceGC();

        #endregion

        #region Sampling

        void BeginSample(string sampleName);
        void EndSample(string sampleName);
        Dictionary<string, PerformanceSample> GetSamples();
        void ClearSamples();

        #endregion

        #region Resources

        ResourceStatistics GetResourceStatistics();

        #endregion

        #region Alerts

        void SetFPSAlert(float threshold, Action<float> callback);
        void SetMemoryAlert(float thresholdMB, Action<float> callback);
        void ClearFPSAlert();
        void ClearMemoryAlert();

        #endregion

        #region Report / Export

        string GenerateReport();
        string ExportData();

        #endregion

        #region History Snapshots

        List<PerformanceDataSnapshot> GetPerformanceDataHistory(int maxCount = 100);
        void ClearPerformanceDataHistory();
        UniTask<bool> SavePerformanceDataAsync(string filePath, CancellationToken ct = default);
        UniTask<PerformanceDataSnapshot> LoadPerformanceDataAsync(string filePath, CancellationToken ct = default);

        #endregion
    }
}
