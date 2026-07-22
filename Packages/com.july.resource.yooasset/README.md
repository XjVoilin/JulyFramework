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

## Mini-game filesystems

The platform adapters compile only when the corresponding project SDK and define are
available:

- WeChat: `UNITY_WEBGL && JULYGF_WX_MINIGAME`
- TikTok: `UNITY_WEBGL && JULYGF_DY_MINIGAME`

Both adapters stay in the public namespace `July.Resource.YooAsset`. YooAsset exposes
its mini-game internals only to the friend assembly named `YooAsset.MiniGame`, so the
two source-level platform branches share that single conditional assembly. Enable it
with `JULYGF_YOOASSET_MINIGAME`; ordinary Unity projects do not compile the adapters.

Use `WeChatYooAssetFileSystem.CreateInitializeParameters(...)` or
`TikTokYooAssetFileSystem.CreateInitializeParameters(...)` as the
`YooAssetOptions.CreateInitializeParameters` factory.
