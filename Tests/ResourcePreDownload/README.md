# Resource predownload regression checks

These checks cover July Resource 0.2.0 / YooAsset adapter 0.3.0 without importing
test logic into a consuming game's runtime code. The public interface is
`StartPreDownloads(tags)`, `PausePreDownloads(exceptTags)` and
`ResumePreDownloads()`; ordinary tag download remains the readiness API.

## Managed checks

Requirements: Python 3, .NET 8 SDK at the standard Windows installation, a resolved
Unity project with YooAsset 2.3.12, UniTask, July and NUnit assemblies. The current
runner uses Unity 2022.3.62f2 at `E:/Install/UnityHub/2022.3.62f2`.

```powershell
python Tests/ResourcePreDownload/run_checks.py --project E:/Projects/Kunpo/GooseMarket --output E:/Projects/Kunpo/GooseMarket/.scratch/july-predownload/validation/source
```

Run from JulyFramework. The package source defaults to this repository's Packages
folder. An explicit `--packages` can select an implementation snapshot.

The runner compiles the resource package, shipping adapter against the installed
YooAsset assembly, and the existing Resource/UI/Scene fixtures that implement the
resource interface. It then compiles the original YooAsset runtime sources with
July's actual adapter/controller and a controlled in-memory file system.
Only file I/O is substituted. No YooAsset source is edited. The managed fixture
sets private initialization fields and UniTask's main-thread identity without
invoking Unity native startup; the separate Play Mode run below exercises actual startup.

The 22 scenarios cover repeated starts, explicit start after pause, in-flight
completion, selective resume, exclusion semantics, cache eviction/reuse, failure
and missing-file retry, multiple required waiters, independent cancellation,
restoring background policy, shutdown, validation, package-level abort and the
normal-download bridge.
Logs and compiler response files are saved under the selected output directory.

## Native Unity checks

```powershell
python Tests/ResourcePreDownload/prepare_native.py --project E:/Projects/Kunpo/GooseMarket
# Run Unity and wait for its process exit; do not pass -quit to the async checks.
E:/Install/UnityHub/2022.3.62f2/Editor/Unity.exe -batchmode -nographics -projectPath E:/Projects/Kunpo/GooseMarket/.scratch/july-predownload/UnityValidation -executeMethod JulyPreDownloadUnityChecks.Run -logFile E:/Projects/Kunpo/GooseMarket/.scratch/july-predownload/native.log
```

The preparation script creates a separate minimal project and does not import into
the game project. It snapshots the adapter without its optional SDK Platforms
folder, reserving YooAsset's existing friend-assembly name `YooAsset.MiniGame` for
the controlled test file system. Shipping adapter/runtime sources are unchanged;
no WeChat/TikTok SDK or network service is called.

The 15 assertions run in actual Play Mode using the real YooAsset operation driver,
July initialization and UniTask player loop: idempotent initialization/manifest,
pause/resume, in-flight completion, required waits, duplicate/cached work, normal
download without prefetch, failed prefetch falling back to ordinary tag retry, and
shutdown cancellation, worker-thread cancellation and public package destruction.
The test-only shutdown invocation uses reflection to call
the existing protected lifecycle hook. `native-result.txt` is written beside the
generated project and Unity exits with 0 only on success.

These results do not verify CDN/HTTP behavior, physical abort, WeChat cache or SDK
behavior, mobile bandwidth/performance, bundle layout, or game integration.
