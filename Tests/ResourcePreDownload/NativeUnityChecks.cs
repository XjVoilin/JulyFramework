using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Resource.YooAsset;
using UnityEditor;
using UnityEngine;
using YooAsset;

[InitializeOnLoad]
public static class JulyPreDownloadUnityChecks
{
    private const string PendingKey = "July.PreDownload.NativeChecks.Pending";
    private static int _frames;
    private static int _passed;
    private static double _deadline;
    private static YooAssetResourceSystem _resources;
    private static void Check(bool success, string description)
    {
        if (!success) throw new Exception(description);
        Debug.Log("PREDOWNLOAD PASS " + description);
        _passed++;
    }
    static JulyPreDownloadUnityChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            StartChecks();
        };
    }
    public static void Run()
    {
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }
    private static void StartChecks()
    {
        _deadline = EditorApplication.timeSinceStartup + 40;
        EditorApplication.update += Pump;
        RunAsync().Forget(error => Finish(error));
    }
    private static void Pump()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Finish(new TimeoutException("Native predownload checks timed out"));
            return;
        }
        _frames++;
    }
    private static UniTask Frames(int count)
    {
        var target = _frames + count;
        return UniTask.WaitUntil(() => _frames >= target);
    }
    private static async UniTask RunAsync()
    {
        _resources = new YooAssetResourceSystem(new YooAssetOptions
        {
            PackageName = "PreDownloadNativeChecks",
            PlayMode = EPlayMode.CustomPlayMode,
            MaxConcurrentDownloads = 1,
            DownloadRetryCount = 0,
            CreateInitializeParameters = _ => new CustomPlayModeParameters
            {
                FileSystemParameterList =
                {
                    new(typeof(NativeCheckFileSystem).AssemblyQualifiedName, "")
                }
            }
        });
        await _resources.InitializeAsync();
        await _resources.InitializeAsync();
        var fs = NativeCheckFileSystem.Current;
        Check(_resources.IsInitialized && _resources.Package.PackageValid && fs.Initializations == 1,
            "real package/manifest initialization is shared");

        _resources.StartPreDownloads("A");
        _resources.StartPreDownloads("A");
        _resources.PausePreDownloads();
        await Frames(3);
        Check(fs.Started.Count == 0, "pause before first dispatch starts no files");
        _resources.StartPreDownloads("A");
        await UniTask.WaitUntil(() => fs.Started.Count == 1);
        _resources.PausePreDownloads();
        fs.Started[0].AllowComplete = true;
        await Frames(3);
        Check(fs.Cached.Count == 1 && fs.Started.Count == 1,
            "in-flight completion does not dispatch another paused file");

        fs.AutoComplete = true;
        var needed = _resources.DownloadByTagWithRetryAsync("A");
        _resources.PausePreDownloads();
        Check(await needed, "normal required wait bypasses background pause");
        Check(fs.Started.Count == 3, "repeated start/normal join use one download operation");
        Check(await _resources.DownloadByTagAsync("A"), "normal download sees completed predownload cache");
        Check(fs.Started.Count == 3, "cached repeat performs no file download");

        var ordinary = _resources.DownloadByTagAsync("B");
        _resources.PausePreDownloads();
        Check(await ordinary, "ordinary download without a predownload retains its real wait path");

        fs.FailuresRemaining = 1;
        _resources.StartPreDownloads("C");
        Check(await _resources.DownloadByTagWithRetryAsync("C", maxRetries: 2),
            "failed joined predownload retries through the normal downloader");
        Check(fs.Started.Count(op => op.Bundle.Tags[0] == "C") == 4,
            "tag retry fills missing files after one controlled failure");

        fs.AutoComplete = false;
        _resources.StartPreDownloads("E");
        _resources.PausePreDownloads();
        using (var cancel = new CancellationTokenSource())
        {
            var canceledThread = ObserveCancellationThreadAsync(
                _resources.DownloadByTagAsync("E", cancel.Token));
            await System.Threading.Tasks.Task.Run(() => cancel.Cancel());
            Check(await canceledThread == PlayerLoopHelper.MainThreadId,
                "worker cancellation returns to main thread before cleanup and caller continuation");
        }
        var startedE = fs.Started.Count(op => op.Bundle.Tags[0] == "E");
        foreach (var op in fs.Started.Where(op => op.Bundle.Tags[0] == "E")) op.AllowComplete = true;
        await Frames(3);
        Check(fs.Started.Count(op => op.Bundle.Tags[0] == "E") == startedE,
            "worker cancellation restores the paused background state");

        fs.AutoComplete = false;
        _resources.StartPreDownloads("D");
        var required = _resources.DownloadByTagWithRetryAsync("D");
        typeof(YooAssetResourceSystem).GetMethod("OnShutdown", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_resources, null);
        var canceled = false;
        try { await required; } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && !_resources.IsInitialized, "shutdown cancels a real joined waiter without retrying");

        await _resources.InitializeAsync();
        _resources.StartPreDownloads("F");
        var aborted = _resources.DownloadByTagAsync("F");
        var destroying = _resources.Package.DestroyAsync();
        await UniTask.WaitUntil(() => destroying.IsDone);
        // 包销毁会先标记Abort，通用完成事件在下一次OperationSystem更新交付。
        await UniTask.WaitUntil(() => aborted.Status.IsCompleted())
            .Timeout(TimeSpan.FromSeconds(3), DelayType.Realtime);
        Check(aborted.Status.IsCompleted(), "package destroy settles an active predownload waiter");
        Check(!await aborted, "package abort is delivered as download failure");
        Finish(null);
    }
    private static async UniTask<int> ObserveCancellationThreadAsync(UniTask<bool> task)
    {
        try { await task; }
        catch (OperationCanceledException) { return Thread.CurrentThread.ManagedThreadId; }
        throw new Exception("Expected cancellation");
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update -= Pump;
        var text = error == null ? $"{_passed} NATIVE UNITY CHECKS PASSED" : error.ToString();
        File.WriteAllText(Path.GetFullPath("../native-result.txt"), text);
        if (error != null) Debug.LogException(error); else Debug.Log(text);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}

internal sealed class NativeCheckFileSystem : IFileSystem
{
    internal static NativeCheckFileSystem Current;
    internal readonly HashSet<string> Cached = new();
    internal readonly List<NativeDownload> Started = new();
    internal bool AutoComplete;
    internal int FailuresRemaining;
    internal int Initializations;
    private PackageManifest _manifest;
    public string PackageName { get; private set; }
    public string FileRoot => "";
    public int FileCount => Cached.Count;
    public void OnCreate(string name, string root)
    {
        Current = this;
        PackageName = name;
        _manifest = new PackageManifest { PackageName = name, PackageVersion = "1" };
        foreach (var tag in new[] { "A", "B", "C", "D", "E", "F" })
            for (var i = 0; i < 3; i++)
                _manifest.BundleList.Add(new PackageBundle
                {
                    BundleName = tag + i, FileHash = tag + i, FileSize = 1, Tags = new[] { tag }
                });
    }
    public FSInitializeFileSystemOperation InitializeFileSystemAsync() { Initializations++; return new Initialize(); }
    public FSRequestPackageVersionOperation RequestPackageVersionAsync(bool append, int timeout) => new Version();
    public FSLoadPackageManifestOperation LoadPackageManifestAsync(string version, int timeout) => new Manifest(_manifest);
    public bool Belong(PackageBundle bundle) => true;
    public bool Exists(PackageBundle bundle) => Cached.Contains(bundle.BundleName);
    public bool NeedDownload(PackageBundle bundle) => !Exists(bundle);
    public bool NeedUnpack(PackageBundle bundle) => false;
    public bool NeedImport(PackageBundle bundle) => false;
    public FSDownloadFileOperation DownloadFileAsync(PackageBundle bundle, DownloadFileOptions options)
    {
        var op = new NativeDownload(bundle, this); Started.Add(op); return op;
    }
    public void SetParameter(string name, object value) => throw new NotSupportedException();
    public void OnDestroy() { }
    public FSClearCacheFilesOperation ClearCacheFilesAsync(PackageManifest manifest, ClearCacheFilesOptions options) => throw new NotSupportedException();
    public FSLoadBundleOperation LoadBundleFile(PackageBundle bundle) => throw new NotSupportedException();
    public string GetBundleFilePath(PackageBundle bundle) => throw new NotSupportedException();
    public byte[] ReadBundleFileData(PackageBundle bundle) => throw new NotSupportedException();
    public string ReadBundleFileText(PackageBundle bundle) => throw new NotSupportedException();
    private sealed class Initialize : FSInitializeFileSystemOperation
    {
        internal override void InternalStart() { }
        internal override void InternalUpdate() => Status = EOperationStatus.Succeed;
    }
    private sealed class Version : FSRequestPackageVersionOperation
    {
        internal override void InternalStart() { }
        internal override void InternalUpdate() { PackageVersion = "1"; Status = EOperationStatus.Succeed; }
    }
    private sealed class Manifest : FSLoadPackageManifestOperation
    {
        internal Manifest(PackageManifest manifest) { base.Manifest = manifest; }
        internal override void InternalStart() { }
        internal override void InternalUpdate() => Status = EOperationStatus.Succeed;
    }
    internal sealed class NativeDownload : FSDownloadFileOperation
    {
        private readonly NativeCheckFileSystem _fs;
        internal bool AllowComplete;
        internal NativeDownload(PackageBundle bundle, NativeCheckFileSystem fs) : base(bundle) { _fs = fs; }
        internal override void InternalStart() { }
        internal override void InternalUpdate()
        {
            if (!AllowComplete && !_fs.AutoComplete) return;
            if (_fs.FailuresRemaining > 0)
            {
                _fs.FailuresRemaining--;
                Error = "controlled native file failure";
                Status = EOperationStatus.Failed;
            }
            else
            {
                _fs.Cached.Add(Bundle.BundleName);
                DownloadedBytes = 1;
                Status = EOperationStatus.Succeed;
            }
        }
    }
}
