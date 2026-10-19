# July Resource - YooAsset

`com.july.resource.yooasset` is the production YooAsset implementation of
`July.Resource.IResourceSystem`.

## Responsibilities

- Creates or reuses a YooAsset `ResourcePackage`.
- Supports explicit early initialization before the Arch lifecycle starts.
- Loads assets and scenes through `ResourceHandle<T>` ownership.
- Downloads resources by tag with cancellation and retry support.
- Supplies optional WeChat and TikTok mini-game filesystem adapters.

The project remains responsible for package names, CDN URLs, play mode, resource tags,
update policy and user-facing failure handling.

## Early initialization

```csharp
var options = new YooAssetOptions
{
    PlayMode = EPlayMode.HostPlayMode,
    DefaultHostServer = cdnUrl,
};

var resources = new YooAssetResourceSystem(options);
await resources.InitializeAsync(cancellationToken);
ArchContext.Current.RegisterSystem(resources);
```

`InitializeAsync` is idempotent. When Arch later initializes the registered system,
the same initialization task is reused.

## Background predownloads

After initialization, projects can submit tags without keeping task handles:

```csharp
resources.StartPreDownloads("Chapter2", "PhotoMode");
resources.PausePreDownloads();
resources.StartPreDownloads("Chapter2"); // start or resume only this tag

// When the content becomes required, use the existing download interface.
bool ready = await resources.DownloadByTagWithRetryAsync("Chapter2", ct: token);
```

Starting an active tag shares its existing operation. A paused tag resumes;
completed or failed entries are removed, so subsequent starts query YooAsset's
actual cache again. Empty start lists do nothing. Invalid tags fail before a
batch starts.

`PausePreDownloads("Chapter2")` skips Chapter2 without changing its previous
state. It does not resume an already paused tag or prohibit future explicit
starts. `ResumePreDownloads()` resumes all existing predownloads. No command
controls ordinary downloads that were started independently.

A normal download joining an active predownload keeps it running while that
caller needs it. Canceling a caller stops only its wait; the operation returns
to the requested background state when no required callers remain. Failed
predownloads are logged; normal tag retries remain the caller's existing option.
Background predownloads use only YooAsset's configured file-level retry.

Completion observes YooAsset's general operation event, including package-level
aborts. Cancellation tokens may be canceled from a worker thread; shared state
cleanup returns to Unity's main thread before the joined call completes. Resource
commands themselves follow the usual Unity main-thread calling requirement.

Pausing stops new file dispatch; in-flight transfers can continue. Shutdown
cancels the controller's operations and shared waits, but does not promise an
immediate physical network abort on every platform. Cache storage remains
owned by YooAsset/the platform. Reverse sharing (ordinary download first,
predownload later) and cross-tag file deduplication are not supplied here.

Projects own the start/pause timing, tags, bundle layout and any failure UI.
No project or game lifecycle is installed by this package.

## Mini-game filesystems

The platform adapters compile only when the corresponding project SDK and define are
available:

- WeChat: `UNITY_WEBGL && JULYGF_WX_MINIGAME`
- TikTok: `UNITY_WEBGL && JULYGF_DY_MINIGAME`

Both adapters stay in the public namespace `July.Resource.YooAsset`. YooAsset exposes
its mini-game internals only to the friend assembly named `YooAsset.MiniGame`, so the
two source-level platform branches share that single conditional assembly. Enable it
with `JULYGF_YOOASSET_MINIGAME`; ordinary Unity projects do not compile the adapters.
Because that friend assembly references both vendor assemblies, a mini-game project
must contain the `Wx` and `TTWebGL` assembly definitions even when it builds only one
of the two platforms. This is a YooAsset 2.3.12 friend-assembly limitation; projects
that cannot embed both SDK definitions should leave the mini-game adapter disabled.

Use `WeChatYooAssetFileSystem.CreateInitializeParameters(...)` or
`TikTokYooAssetFileSystem.CreateInitializeParameters(...)` as the
`YooAssetOptions.CreateInitializeParameters` factory.

Predownloads log `[July.Resource.Download] [tag] PreDownload` with Started,
Progress, Ready (cached), Succeeded, Failed or Canceled. Progress is limited
to one line per second; final states log immediately. Logs use JLogger directly and follow
its JULYGF_DEBUG switch, without an Editor panel or injected log callback. Ordinary downloads keep their existing behavior.
