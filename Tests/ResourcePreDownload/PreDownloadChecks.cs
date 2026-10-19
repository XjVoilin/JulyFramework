using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Resource;
using July.Resource.YooAsset;
using YooAsset;

// Standalone checks compile the unmodified YooAsset runtime sources alongside
// July. Only file I/O is controlled; scheduling, tags/cache filtering, download
// operations, controller and the resource-system bridge are real code.
internal static class PreDownloadChecks
{
    private static int _passed;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Canceled(UniTask<bool> task)
    {
        Check(task.Status.IsCompleted(), "Cancellation must settle the waiter");
        Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
    }
    private static void Run(string name, Action<Fixture> test)
    {
        OperationSystem.DestroyAll();
        OperationSystem.Initialize();
        using var f = new Fixture();
        test(f);
        Console.WriteLine("PASS " + name);
        _passed++;
    }

    public static int Main()
    {
        // Managed fixture models Unity's main-thread initialization without native startup.
        typeof(PlayerLoopHelper).GetField("mainThreadId", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, Thread.CurrentThread.ManagedThreadId);
        YooLogger.Logger = new QuietYooLogger();
        try
        {
            Run("batch start and duplicate start share active work", f =>
            {
                f.Controller.Start(new[] { "A", "B", "A" });
                f.Controller.Start(new[] { "A" });
                f.Controller.Start(Array.Empty<string>());
                Check(f.Created == 2, "Must create one operation per active tag");
            });
            Run("pause is not a ban on future explicit start", f =>
            {
                f.Controller.Pause(Array.Empty<string>());
                f.Controller.Start(new[] { "A" });
                f.Tick();
                Check(f.FS.Started.Count == 1, "Explicit start must run after pause");
            });
            Run("pause drains in-flight file without dispatching another", f =>
            {
                f.Controller.Start(new[] { "A" }); f.Tick();
                f.Controller.Pause(Array.Empty<string>());
                f.FinishStarted(); f.Tick();
                Check(f.FS.Cached.Count == 1, "In-flight success must be cached");
                Check(f.FS.Started.Count == 1, "Paused operation must not dispatch more");
                Check(f.Controller.TryGet("A", out _), "Partial paused work remains active");
            });
            Run("start resumes only the requested paused tag", f =>
            {
                f.Controller.Start(new[] { "A", "B" }); f.Tick();
                f.Controller.Pause(Array.Empty<string>());
                f.FinishStarted(); f.Tick();
                f.Controller.Start(new[] { "A" }); f.Tick();
                Check(f.FS.Started.Count(o => o.Tag == "A") == 2, "A resumes");
                Check(f.FS.Started.Count(o => o.Tag == "B") == 1, "B stays paused");
                Check(f.Created == 2, "Resume must not create another operation");
            });
            Run("pause exclusion does not resume an already paused tag", f =>
            {
                f.Controller.Start(new[] { "A", "B" }); f.Tick();
                f.Controller.Pause(Array.Empty<string>());
                f.FinishStarted(); f.Tick();
                f.Controller.Pause(new[] { "A" }); f.Tick();
                Check(f.FS.Started.Count == 2, "Excluded paused A stays paused");
                f.Controller.Resume(); f.Tick();
                Check(f.FS.Started.Count == 4, "Explicit resume restores both");
            });
            Run("pause exclusion leaves running target running", f =>
            {
                f.Controller.Start(new[] { "A", "B" }); f.Tick();
                f.Controller.Pause(new[] { "A" });
                f.FinishStarted(); f.Tick();
                Check(f.FS.Started.Count(o => o.Tag == "A") == 2, "A keeps running");
                Check(f.FS.Started.Count(o => o.Tag == "B") == 1, "B pauses");
            });
            Run("completion removes activity; repeat checks actual cache", f =>
            {
                f.Controller.Start(new[] { "A" }); f.CompleteAll();
                Check(!f.Controller.TryGet("A", out _), "Success leaves no permanent registry entry");
                var started = f.FS.Started.Count;
                f.Controller.Start(new[] { "A" });
                Check(f.Created == 2 && !f.Controller.TryGet("A", out _), "Cached start settles immediately");
                Check(f.FS.Started.Count == started, "Cached files are not downloaded again");
                f.FS.Cached.Remove("A0");
                f.Controller.Start(new[] { "A" }); f.CompleteAll();
                Check(f.FS.Started.Count == started + 1, "Cache eviction triggers real missing-file download");
            });
            Run("failure is reported and new start downloads only missing files", f =>
            {
                f.Controller.Start(new[] { "A" }); f.Tick();
                f.FinishStarted(); f.Tick(); // one success and next dispatch
                f.FinishStarted(fail: true); f.Tick();
                Check(f.Errors.Count == 1 && !f.Controller.TryGet("A", out _), "Final failure is visible and removed");
                f.Controller.Start(new[] { "A" }); f.CompleteAll();
                Check(f.FS.Cached.Count == 3, "Retry fills missing files");
                Check(f.FS.Started.Count == 4, "Successful first file is not repeated");
            });
            Run("multiple required waiters bypass background pause", f =>
            {
                f.Controller.Start(new[] { "A" });
                f.Controller.TryGet("A", out var entry);
                var a = entry.WaitForUseAsync(default);
                var b = entry.WaitForUseAsync(default);
                f.Controller.Pause(Array.Empty<string>());
                f.CompleteAll();
                Check(a.GetAwaiter().GetResult() && b.GetAwaiter().GetResult(), "All waiters receive the same success");
                Check(f.Created == 1, "Foreground demand shares predownload");
            });
            Run("canceling one waiter leaves another required waiter running", f =>
            {
                f.Controller.Start(new[] { "A" });
                f.Controller.TryGet("A", out var entry);
                using var cancel = new CancellationTokenSource();
                var a = entry.WaitForUseAsync(cancel.Token);
                var b = entry.WaitForUseAsync(default);
                f.Controller.Pause(Array.Empty<string>()); cancel.Cancel();
                Canceled(a); f.CompleteAll();
                Check(b.GetAwaiter().GetResult(), "Second waiter survives cancellation");
            });
            Run("last required cancellation restores background pause", f =>
            {
                f.Controller.Start(new[] { "A" });
                f.Controller.TryGet("A", out var entry);
                f.Controller.Pause(Array.Empty<string>());
                using var cancel = new CancellationTokenSource();
                var wait = entry.WaitForUseAsync(cancel.Token); f.Tick();
                cancel.Cancel(); Canceled(wait);
                f.FinishStarted(); f.Tick();
                Check(f.FS.Started.Count == 1 && f.Controller.TryGet("A", out _), "Remain paused, not canceled");
                f.Controller.Start(new[] { "A" }); f.CompleteAll();
                Check(f.FS.Cached.Count == 3, "Remaining work can resume");
            });
            Run("canceled token never resumes paused work", f =>
            {
                f.Controller.Start(new[] { "A" });
                f.Controller.TryGet("A", out var entry);
                f.Controller.Pause(Array.Empty<string>());
                Canceled(entry.WaitForUseAsync(new CancellationToken(true))); f.Tick();
                Check(f.FS.Started.Count == 0, "Canceled wait starts no files");
            });
            Run("waiter cancellation does not pause a running background request", f =>
            {
                f.Controller.Start(new[] { "A" });
                f.Controller.TryGet("A", out var entry);
                using var cancel = new CancellationTokenSource();
                var wait = entry.WaitForUseAsync(cancel.Token);
                cancel.Cancel(); Canceled(wait); f.CompleteAll();
                Check(f.FS.Cached.Count == 3, "Background policy remains running");
            });
            Run("shutdown settles all waiters without depending on native finish callback", f =>
            {
                f.Controller.Start(new[] { "A", "B" });
                f.Controller.TryGet("A", out var a);
                f.Controller.TryGet("B", out var b);
                var first = a.WaitForUseAsync(default);
                var second = a.WaitForUseAsync(default);
                var third = b.WaitForUseAsync(default); f.Tick();
                f.Controller.Shutdown();
                Canceled(first); Canceled(second); Canceled(third);
                Check(!f.Controller.TryGet("A", out _) && !f.Controller.TryGet("B", out _), "Shutdown clears activity");
                Check(f.Native.All(o => o.Status == EOperationStatus.Failed), "Active native operations canceled");
                Check(f.FS.Started.All(o => o.ReleaseCount == 1), "Active file references released once");
                f.Controller.Shutdown();
                Throws<ObjectDisposedException>(() => f.Controller.Start(new[] { "A" }));
                Throws<ObjectDisposedException>(() => f.Controller.Pause(Array.Empty<string>()));
                Throws<ObjectDisposedException>(() => f.Controller.Resume());
            });
            Run("invalid batch fails before creating partial work", f =>
            {
                Throws<ArgumentNullException>(() => f.Controller.Start(null));
                Throws<ArgumentException>(() => f.Controller.Start(new[] { "A", " " }));
                Throws<ArgumentException>(() => f.Controller.Pause(new[] { "" }));
                Check(f.Created == 0, "Invalid request creates no tasks");
            });
            Run("resource-system normal download joins the predownload", f =>
            {
                f.Resources.StartPreDownloads("A");
                f.Resources.PausePreDownloads();
                var wait = f.Resources.DownloadByTagWithRetryAsync("A", maxRetries: 1);
                f.Resources.PausePreDownloads(); f.CompleteAll();
                Check(wait.GetAwaiter().GetResult() && f.Created == 1, "Normal API reuses and resumes the task");
                Check(f.Resources.DownloadByTagAsync("A").GetAwaiter().GetResult(), "Completed preload is visible through normal cache path");
                Check(f.FS.Started.Count == 3, "Normal cache check repeats no file downloads");
            });
            Run("resource-system bridge reports failure and accepts a fresh attempt", f =>
            {
                f.Resources.StartPreDownloads("A");
                var first = f.Resources.DownloadByTagWithRetryAsync("A", maxRetries: 1);
                f.Tick(); f.FinishStarted(fail: true); f.Tick();
                Check(!first.GetAwaiter().GetResult(), "Actual false reaches existing retry API");
                f.Resources.StartPreDownloads("A");
                var second = f.Resources.DownloadByTagWithRetryAsync("A", maxRetries: 1);
                f.CompleteAll();
                Check(second.GetAwaiter().GetResult() && f.Created == 2, "Fresh attempt cannot attach to failed entry");
            });
            Run("already-canceled normal request creates no work", f =>
            {
                Canceled(f.Resources.DownloadByTagAsync("A", new CancellationToken(true)));
                Check(f.Created == 0 && f.FS.Started.Count == 0, "Cancellation checked before native creation");
            });
            Run("resource-system shutdown ends joined wait and rejects new predownload", f =>
            {
                f.Resources.StartPreDownloads("A");
                var wait = f.Resources.DownloadByTagWithRetryAsync("A");
                typeof(YooAssetResourceSystem).GetMethod("OnShutdown", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(f.Resources, null);
                Canceled(wait);
                Throws<InvalidOperationException>(() => f.Resources.StartPreDownloads("A"));
            });
            Run("uninitialized resources reject predownload commands", f =>
            {
                var resources = new YooAssetResourceSystem(new YooAssetOptions());
                Throws<InvalidOperationException>(() => resources.StartPreDownloads("A"));
                Throws<InvalidOperationException>(() => resources.PausePreDownloads());
                Throws<InvalidOperationException>(() => resources.ResumePreDownloads());
            });
            Run("Resources implementation requires no network or lifecycle for download commands", f =>
            {
                IResourceSystem resources = new UnityResourceSystem();
                resources.StartPreDownloads("A"); resources.PausePreDownloads(); resources.ResumePreDownloads();
                Check(resources.DownloadByTagAsync("A").GetAwaiter().GetResult(), "Resources mode is immediately ready");
            });
            Run("package-level abort settles joined wait instead of hanging", f =>
            {
                f.Resources.StartPreDownloads("A");
                var wait = f.Resources.DownloadByTagAsync("A");
                f.Tick();
                OperationSystem.ClearPackageOperation("Checks");
                f.Tick();
                Check(wait.Status.IsCompleted(), "Aborted native operation must settle its shared wait");
                Check(!wait.GetAwaiter().GetResult(), "Native abort reaches normal download as failure");
                Check(!f.Controller.TryGet("A", out _), "Aborted task leaves no stale active entry");
            });
            Console.WriteLine($"{_passed} CHECKS PASSED");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class QuietYooLogger : ILogger
    {
        public void Log(string message) { }
        public void Warning(string message) { }
        public void Error(string message) => throw new Exception(message);
        public void Exception(Exception error) => throw error;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MemoryFileSystem FS = new();
        internal readonly List<string> Errors = new();
        internal readonly List<ResourceDownloaderOperation> Native = new();
        internal readonly YooAssetPreDownloadController Controller;
        internal readonly YooAssetResourceSystem Resources = new(new YooAssetOptions());
        internal int Created => Native.Count;
        internal Fixture()
        {
            var manifest = new PackageManifest { PackageName = "Checks", PackageVersion = "1" };
            foreach (var tag in new[] { "A", "B" })
                for (var i = 0; i < 3; i++)
                    manifest.BundleList.Add(new PackageBundle
                    {
                        BundleName = tag + i, FileHash = tag + i, FileSize = 1, Tags = new[] { tag }
                    });
            var mode = new PlayModeImpl("Checks", EPlayMode.HostPlayMode) { ActiveManifest = manifest };
            mode.FileSystems.Add(FS);
            var package = new ResourcePackage("Checks");
            Set(package, "_initializeStatus", EOperationStatus.Succeed);
            Set(package, "_playModeImpl", mode);
            Controller = new YooAssetPreDownloadController(tag =>
            {
                var native = package.CreateResourceDownloader(tag, 1, 0);
                Native.Add(native);
                return native;
            }, Errors.Add, _ => { });
            Set(Resources, "<Package>k__BackingField", package);
            Set(Resources, "_preDownloads", Controller);
        }
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        internal void Tick() => OperationSystem.Update();
        internal void FinishStarted(bool fail = false)
        {
            foreach (var op in FS.Started.Where(op => !op.IsDone)) op.Outcome = fail ? -1 : 1;
        }
        internal void CompleteAll()
        {
            for (var i = 0; i < 8; i++) { FinishStarted(); Tick(); }
        }
        public void Dispose() { Controller.Shutdown(); OperationSystem.DestroyAll(); }
    }

    private sealed class MemoryFileSystem : IFileSystem
    {
        internal readonly HashSet<string> Cached = new();
        internal readonly List<ManualDownload> Started = new();
        public string PackageName => "Checks";
        public string FileRoot => "";
        public int FileCount => Cached.Count;
        public bool Belong(PackageBundle bundle) => true;
        public bool Exists(PackageBundle bundle) => Cached.Contains(bundle.BundleName);
        public bool NeedDownload(PackageBundle bundle) => !Exists(bundle);
        public bool NeedUnpack(PackageBundle bundle) => false;
        public bool NeedImport(PackageBundle bundle) => false;
        public FSDownloadFileOperation DownloadFileAsync(PackageBundle bundle, DownloadFileOptions options)
        {
            var op = new ManualDownload(bundle, this); Started.Add(op); return op;
        }
        public void OnCreate(string name, string root) { }
        public void OnDestroy() { }
        public void SetParameter(string name, object value) => throw new NotSupportedException();
        public FSInitializeFileSystemOperation InitializeFileSystemAsync() => throw new NotSupportedException();
        public FSLoadPackageManifestOperation LoadPackageManifestAsync(string version, int timeout) => throw new NotSupportedException();
        public FSRequestPackageVersionOperation RequestPackageVersionAsync(bool append, int timeout) => throw new NotSupportedException();
        public FSClearCacheFilesOperation ClearCacheFilesAsync(PackageManifest manifest, ClearCacheFilesOptions options) => throw new NotSupportedException();
        public FSLoadBundleOperation LoadBundleFile(PackageBundle bundle) => throw new NotSupportedException();
        public string GetBundleFilePath(PackageBundle bundle) => throw new NotSupportedException();
        public byte[] ReadBundleFileData(PackageBundle bundle) => throw new NotSupportedException();
        public string ReadBundleFileText(PackageBundle bundle) => throw new NotSupportedException();
    }

    private sealed class ManualDownload : FSDownloadFileOperation
    {
        private readonly MemoryFileSystem _fs;
        internal int Outcome;
        internal int ReleaseCount;
        internal string Tag => Bundle.Tags[0];
        internal ManualDownload(PackageBundle bundle, MemoryFileSystem fs) : base(bundle) { _fs = fs; }
        internal override void InternalStart() { }
        internal override void InternalUpdate()
        {
            if (Outcome == 0) return;
            if (Outcome > 0)
            {
                _fs.Cached.Add(Bundle.BundleName);
                DownloadedBytes = 1;
                Status = EOperationStatus.Succeed;
            }
            else { Status = EOperationStatus.Failed; Error = "controlled file failure"; }
        }
        public override void Release() { ReleaseCount++; base.Release(); }
    }
}
