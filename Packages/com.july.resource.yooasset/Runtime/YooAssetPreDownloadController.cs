using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace July.Resource.YooAsset
{
    // 只持有活动预下载；普通下载和文件缓存不归此对象所有。
    internal sealed class YooAssetPreDownloadController
    {
        private readonly Func<string, ResourceDownloaderOperation> _createDownloader;
        private readonly Action<string> _reportFailure;
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private bool _isShutdown;

        internal YooAssetPreDownloadController(
            Func<string, ResourceDownloaderOperation> createDownloader, Action<string> reportFailure)
        {
            _createDownloader = createDownloader;
            _reportFailure = reportFailure;
        }

        internal void Start(string[] tags)
        {
            EnsureActive();
            ValidateTags(tags);
            foreach (var tag in tags)
            {
                if (!_entries.TryGetValue(tag, out var entry))
                {
                    entry = new Entry(tag, _createDownloader(tag), this);
                    _entries.Add(tag, entry);
                }
                entry.SetBackgroundPaused(false);
            }
        }

        internal void Pause(string[] exceptTags)
        {
            EnsureActive();
            ValidateTags(exceptTags);
            foreach (var entry in _entries.Values)
                if (Array.IndexOf(exceptTags, entry.Tag) < 0)
                    entry.SetBackgroundPaused(true);
        }

        internal void Resume()
        {
            EnsureActive();
            foreach (var entry in _entries.Values)
                entry.SetBackgroundPaused(false);
        }

        internal bool TryGet(string tag, out Entry entry) => _entries.TryGetValue(tag, out entry);

        internal void Shutdown()
        {
            if (_isShutdown) return;
            _isShutdown = true;
            var entries = new List<Entry>(_entries.Values);
            _entries.Clear();
            foreach (var entry in entries)
                entry.Shutdown();
        }

        private void Complete(Entry entry, bool succeeded, string error)
        {
            _entries.Remove(entry.Tag);
            if (!succeeded)
                _reportFailure($"预下载失败 [{entry.Tag}]：{error}");
        }

        private void EnsureActive()
        {
            if (_isShutdown)
                throw new ObjectDisposedException(nameof(YooAssetPreDownloadController));
        }

        private static void ValidateTags(string[] tags)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            foreach (var tag in tags)
                if (string.IsNullOrWhiteSpace(tag))
                    throw new ArgumentException("Tag 不能为空。", nameof(tags));
        }

        internal sealed class Entry
        {
            private readonly ResourceDownloaderOperation _downloader;
            private readonly YooAssetPreDownloadController _owner;
            private readonly UniTaskCompletionSource<bool> _completion = new();
            private bool _backgroundPaused;
            private bool _completed;
            private int _requiredWaiters;

            internal string Tag { get; }

            internal Entry(string tag, ResourceDownloaderOperation downloader,
                YooAssetPreDownloadController owner)
            {
                Tag = tag;
                _downloader = downloader;
                _owner = owner;
                _downloader.Completed += OnCompleted;
            }

            internal void SetBackgroundPaused(bool paused)
            {
                _backgroundPaused = paused;
                ApplyState();
            }

            internal async UniTask<bool> WaitForUseAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                _requiredWaiters++;
                try
                {
                    ApplyState();
                    return await _completion.Task.AttachExternalCancellation(ct);
                }
                finally
                {
                    // 外部令牌可能在线程池取消，状态和原生操作仍在Unity主线程收尾。
                    await UniTask.SwitchToMainThread();
                    _requiredWaiters--;
                    ApplyState();
                }
            }

            private void ApplyState()
            {
                if (_completed) return;
                if (_backgroundPaused && _requiredWaiters == 0)
                {
                    _downloader.PauseDownload();
                    return;
                }

                if (_downloader.TotalDownloadCount == 0)
                {
                    Complete(true);
                    return;
                }
                // YooAsset BeginDownload自身幂等，不再维护第二份启动状态。
                _downloader.BeginDownload();
                _downloader.ResumeDownload();
            }

            // 通用Completed也覆盖包销毁造成的Abort，下载专属回调不覆盖该路径。
            private void OnCompleted(AsyncOperationBase operation) =>
                Complete(operation.Status == EOperationStatus.Succeed);

            private void Complete(bool succeeded)
            {
                if (_completed) return;
                _completed = true;
                _downloader.Completed -= OnCompleted;
                // 先移表，再交付结果；失败后的同步重试不会接回旧条目。
                _owner.Complete(this, succeeded, _downloader.Error);
                _completion.TrySetResult(succeeded);
            }

            internal void Shutdown()
            {
                if (_completed) return;
                _completed = true;
                _downloader.Completed -= OnCompleted;
                _downloader.CancelDownload();
                // 关闭后不保证还有下一次PlayerLoop，主动结束全部共享等待。
                _completion.TrySetCanceled();
            }
        }
    }
}
