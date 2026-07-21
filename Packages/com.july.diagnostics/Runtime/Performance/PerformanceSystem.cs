using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace July.Diagnostics
{
    public class PerformanceSystem : SystemBase, IPerformanceSystem, IUpdatableSystem
    {
        #region Fields

        private float _fpsAlertThreshold;
        private Action<float> _fpsAlertCallback;
        private bool _fpsAlertTriggered;

        private float _memoryAlertThreshold;
        private Action<float> _memoryAlertCallback;
        private bool _memoryAlertTriggered;

        private readonly Queue<float> _fpsHistory = new();
        private const int MaxFPSHistorySize = 300;

        private float _currentFPS;
        private float _averageFPS;
        private float _minFPS = float.MaxValue;
        private float _maxFPS;
        private float _fpsAccumulator;
        private int _fpsFrameCount;
        private float _fpsUpdateInterval = 0.5f;
        private float _fpsTimer;

        private readonly Dictionary<string, PerformanceSample> _samples = new();
        private readonly Dictionary<string, long> _activeSampleTimestamps = new();

        private readonly List<PerformanceDataSnapshot> _snapshotHistory = new();
        private const int MaxSnapshotHistorySize = 720;
        private const float SnapshotInterval = 5f;
        private float _snapshotTimer;

        private int _lastGCCount;
        private float _lastGCTime;
        private float _gcFrequency;

        #endregion

        public void OnUpdate(float deltaTime)
        {
            UpdateFPS(deltaTime);
            UpdateGCTracking();
            UpdateAutoSnapshot(deltaTime);
            CheckAlerts();
        }

        #region FPS

        public float CurrentFPS => _currentFPS;
        public float AverageFPS => _averageFPS;
        public float MinFPS => _minFPS == float.MaxValue ? 0f : _minFPS;
        public float MaxFPS => _maxFPS;

        public List<float> GetFPSHistory(int count = 60)
        {
            var list = new List<float>(_fpsHistory);
            if (list.Count > count)
                return list.GetRange(list.Count - count, count);
            return list;
        }

        private void UpdateFPS(float deltaTime)
        {
            _fpsAccumulator += deltaTime;
            _fpsFrameCount++;
            _fpsTimer += deltaTime;

            if (_fpsTimer >= _fpsUpdateInterval && _fpsFrameCount > 0)
            {
                _currentFPS = _fpsFrameCount / _fpsAccumulator;
                _averageFPS = (_averageFPS * 0.9f) + (_currentFPS * 0.1f);
                if (_currentFPS < _minFPS) _minFPS = _currentFPS;
                if (_currentFPS > _maxFPS) _maxFPS = _currentFPS;

                _fpsHistory.Enqueue(_currentFPS);
                while (_fpsHistory.Count > MaxFPSHistorySize) _fpsHistory.Dequeue();

                _fpsAccumulator = 0f;
                _fpsFrameCount = 0;
                _fpsTimer = 0f;
            }
        }

        #endregion

        #region Memory

        private const float ByteToMB = 1f / (1024f * 1024f);

        public float TotalMemoryMB => Profiler.GetTotalReservedMemoryLong() * ByteToMB;
        public float AllocatedMemoryMB => Profiler.GetTotalAllocatedMemoryLong() * ByteToMB;
        public float ReservedMemoryMB => Profiler.GetTotalReservedMemoryLong() * ByteToMB;
        public float MonoHeapSizeMB => Profiler.GetMonoHeapSizeLong() * ByteToMB;
        public float MonoUsedSizeMB => Profiler.GetMonoUsedSizeLong() * ByteToMB;
        public float GCTotalAllocatedMB => GC.GetTotalMemory(false) * ByteToMB;

        public MemorySnapshot GetMemorySnapshot()
        {
            return new MemorySnapshot
            {
                TotalMemoryMB = TotalMemoryMB,
                AllocatedMemoryMB = AllocatedMemoryMB,
                ReservedMemoryMB = ReservedMemoryMB,
                MonoHeapSizeMB = MonoHeapSizeMB,
                MonoUsedSizeMB = MonoUsedSizeMB,
                GCTotalAllocatedMB = GCTotalAllocatedMB,
                Timestamp = DateTime.Now
            };
        }

        public GCStatistics GetGCStatistics()
        {
            return new GCStatistics
            {
                TotalAllocatedMB = GCTotalAllocatedMB,
                MonoHeapSizeMB = MonoHeapSizeMB,
                MonoUsedSizeMB = MonoUsedSizeMB,
                GCCount = GC.CollectionCount(0),
                LastGCTime = _lastGCTime,
                GCFrequency = _gcFrequency
            };
        }

        public void ForceGC()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void UpdateGCTracking()
        {
            var currentGCCount = GC.CollectionCount(0);
            if (currentGCCount != _lastGCCount)
            {
                var now = Time.realtimeSinceStartup;
                if (_lastGCTime > 0f)
                    _gcFrequency = now - _lastGCTime;
                _lastGCTime = now;
                _lastGCCount = currentGCCount;
            }
        }

        #endregion

        #region Sampling

        public void BeginSample(string sampleName)
        {
            UnityEngine.Profiling.Profiler.BeginSample(sampleName);
            _activeSampleTimestamps[sampleName] = Stopwatch.GetTimestamp();
        }

        public void EndSample(string sampleName)
        {
            UnityEngine.Profiling.Profiler.EndSample();

            if (!_activeSampleTimestamps.TryGetValue(sampleName, out var startTicks))
                return;

            _activeSampleTimestamps.Remove(sampleName);
            var elapsedMs = (float)(Stopwatch.GetTimestamp() - startTicks) / Stopwatch.Frequency * 1000f;

            if (!_samples.TryGetValue(sampleName, out var sample))
            {
                sample = new PerformanceSample
                {
                    Name = sampleName,
                    MinTime = float.MaxValue,
                };
                _samples[sampleName] = sample;
            }

            sample.CallCount++;
            sample.TotalTime += elapsedMs;
            sample.AverageTime = sample.TotalTime / sample.CallCount;
            if (elapsedMs < sample.MinTime) sample.MinTime = elapsedMs;
            if (elapsedMs > sample.MaxTime) sample.MaxTime = elapsedMs;
        }

        public Dictionary<string, PerformanceSample> GetSamples() => new(_samples);
        public void ClearSamples() => _samples.Clear();

        #endregion

        #region Resources

        public ResourceStatistics GetResourceStatistics()
        {
            var textures = Resources.FindObjectsOfTypeAll<Texture>();
            var audioClips = Resources.FindObjectsOfTypeAll<AudioClip>();
            var meshes = Resources.FindObjectsOfTypeAll<Mesh>();

            float texMem = 0f;
            foreach (var tex in textures)
                texMem += Profiler.GetRuntimeMemorySizeLong(tex);

            float audioMem = 0f;
            foreach (var clip in audioClips)
                audioMem += Profiler.GetRuntimeMemorySizeLong(clip);

            float meshMem = 0f;
            foreach (var mesh in meshes)
                meshMem += Profiler.GetRuntimeMemorySizeLong(mesh);

            return new ResourceStatistics
            {
                TextureCount = textures.Length,
                TextureMemoryMB = texMem * ByteToMB,
                AudioClipCount = audioClips.Length,
                AudioClipMemoryMB = audioMem * ByteToMB,
                MeshCount = meshes.Length,
                MeshMemoryMB = meshMem * ByteToMB,
                MaterialCount = Resources.FindObjectsOfTypeAll<Material>().Length,
                GameObjectCount = Resources.FindObjectsOfTypeAll<GameObject>().Length,
            };
        }

        #endregion

        #region Alerts

        public void SetFPSAlert(float threshold, Action<float> callback)
        {
            _fpsAlertThreshold = threshold;
            _fpsAlertCallback = callback;
            _fpsAlertTriggered = false;
        }

        public void SetMemoryAlert(float thresholdMB, Action<float> callback)
        {
            _memoryAlertThreshold = thresholdMB;
            _memoryAlertCallback = callback;
            _memoryAlertTriggered = false;
        }

        public void ClearFPSAlert()
        {
            _fpsAlertThreshold = 0f;
            _fpsAlertCallback = null;
            _fpsAlertTriggered = false;
        }

        public void ClearMemoryAlert()
        {
            _memoryAlertThreshold = 0f;
            _memoryAlertCallback = null;
            _memoryAlertTriggered = false;
        }

        private void CheckAlerts()
        {
            if (_fpsAlertThreshold > 0f && _fpsAlertCallback != null)
            {
                if (_currentFPS < _fpsAlertThreshold && !_fpsAlertTriggered)
                {
                    _fpsAlertTriggered = true;
                    _fpsAlertCallback.Invoke(_currentFPS);
                }
                else if (_currentFPS >= _fpsAlertThreshold) _fpsAlertTriggered = false;
            }

            if (_memoryAlertThreshold > 0f && _memoryAlertCallback != null)
            {
                var mem = TotalMemoryMB;
                if (mem > _memoryAlertThreshold && !_memoryAlertTriggered)
                {
                    _memoryAlertTriggered = true;
                    _memoryAlertCallback.Invoke(mem);
                }
                else if (mem <= _memoryAlertThreshold) _memoryAlertTriggered = false;
            }
        }

        #endregion

        #region Report / Export

        public string GenerateReport()
        {
            var report = new StringBuilder();
            report.AppendLine("=== Performance Report ===");
            report.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();
            report.AppendLine(
                $"FPS: current={_currentFPS:F1} avg={_averageFPS:F1} min={MinFPS:F1} max={_maxFPS:F1}");

            var mem = GetMemorySnapshot();
            report.AppendLine(
                $"Memory: total={mem.TotalMemoryMB:F1}MB alloc={mem.AllocatedMemoryMB:F1}MB mono={mem.MonoUsedSizeMB:F1}/{mem.MonoHeapSizeMB:F1}MB");

            if (_samples.Count > 0)
            {
                report.AppendLine("Samples:");
                foreach (var kvp in _samples.OrderByDescending(s => s.Value.TotalTime))
                    report.AppendLine(
                        $"  {kvp.Value.Name}: {kvp.Value.CallCount}x avg={kvp.Value.AverageTime:F3}ms total={kvp.Value.TotalTime:F3}ms");
            }

            return report.ToString();
        }

        public string ExportData()
        {
            var snapshot = CreateSnapshot();
            return JsonUtility.ToJson(snapshot, true);
        }

        #endregion

        #region History Snapshots

        public List<PerformanceDataSnapshot> GetPerformanceDataHistory(int maxCount = 100)
        {
            if (_snapshotHistory.Count <= maxCount)
                return new List<PerformanceDataSnapshot>(_snapshotHistory);

            return _snapshotHistory.GetRange(_snapshotHistory.Count - maxCount, maxCount);
        }

        public void ClearPerformanceDataHistory()
        {
            _snapshotHistory.Clear();
        }

        public async UniTask<bool> SavePerformanceDataAsync(string filePath, CancellationToken ct = default)
        {
            try
            {
                var json = ExportData();
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                await File.WriteAllTextAsync(filePath, json, ct);
                return true;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[PerformanceSystem] Save failed: {ex.Message}");
                return false;
            }
        }

        public async UniTask<PerformanceDataSnapshot> LoadPerformanceDataAsync(string filePath,
            CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                var json = await File.ReadAllTextAsync(filePath, ct);
                return JsonUtility.FromJson<PerformanceDataSnapshot>(json);
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[PerformanceSystem] Load failed: {ex.Message}");
                return null;
            }
        }

        private void UpdateAutoSnapshot(float deltaTime)
        {
            _snapshotTimer += deltaTime;
            if (_snapshotTimer >= SnapshotInterval)
            {
                _snapshotTimer = 0f;
                _snapshotHistory.Add(CreateSnapshot());

                while (_snapshotHistory.Count > MaxSnapshotHistorySize)
                    _snapshotHistory.RemoveAt(0);
            }
        }

        private PerformanceDataSnapshot CreateSnapshot()
        {
            return new PerformanceDataSnapshot
            {
                Timestamp = DateTime.Now,
                CurrentFPS = _currentFPS,
                AverageFPS = _averageFPS,
                MinFPS = MinFPS,
                MaxFPS = _maxFPS,
                Memory = GetMemorySnapshot(),
                GC = GetGCStatistics(),
                Resources = GetResourceStatistics()
            };
        }

        #endregion
    }
}
