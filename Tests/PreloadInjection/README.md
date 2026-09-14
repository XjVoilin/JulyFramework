# Preload injection regression checks

Run from any directory with Node.js and the .NET 8 SDK installed. The Unity project must have resolved `com.july.json` and contain the WeChat and TikTok SDK templates.

```powershell
node <JulyFramework>/Tests/PreloadInjection/verify.mjs <UnityProject> [scratch-output-directory]
```

The runner compiles the actual C# injection methods with small build-context stubs, generates both platforms' launch scripts, then executes those scripts in Node's VM with controlled SDK/network boundaries. Generated output defaults to a temporary directory; no Unity project settings or packages are modified.

Checks cover parallel startup, both list/module arrival orders, one-time dispatch, removal of the old native URL, clearing static/sparse lists, preservation of existing lifecycle callback bodies, repeated injection, invalid responses, network failure, missing bridge APIs, SDK dispatch exceptions, and the unchanged WeChat dynamic submission path.

These checks do not run the Unity Editor test suite or measure real-device downloads. The internal TikTok message path was separately verified on UnityPlugin 4.32.0; recheck it when upgrading that plugin. A `submitted` summary means dispatch returned, not that downloads succeeded. Confirm actual download/cache behavior through SDK `JSFW_PreloadManager` logs.
