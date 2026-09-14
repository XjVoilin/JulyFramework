import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';

import os from 'node:os';
import { fileURLToPath } from 'node:url';
const project = path.resolve(process.argv[2] || '.');
const root = process.argv[3] ? path.resolve(process.argv[3]) : fs.mkdtempSync(path.join(os.tmpdir(), 'july-preload-'));
fs.mkdirSync(root, { recursive: true });
const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const runtime = path.join(repository, 'Packages/com.july.release/Runtime').replaceAll('\\', '/');
const cache = path.join(project, 'Library/PackageCache');
const jsonPackage = fs.readdirSync(cache).find(name => name.startsWith('com.july.json@'));
assert(jsonPackage, 'Resolve the Unity project packages before running this check');
const litJson = path.join(cache, jsonPackage, 'Runtime/Plugins/LitJson.dll').replaceAll('\\', '/');
const source = fs.readFileSync(path.join(repository, 'Packages/com.july.release/Editor/Steps/PreloadInjectionStep.cs'), 'utf8').replace(/\r\n/g, '\n');
for (const template of ['Assets/WX-WASM-SDK-V2/Runtime/wechat-default/game.js', 'Assets/Plugins/ByteGame/com.bytedance.starksdk/DefaultTemplate/game.js']) {
  const text = fs.readFileSync(path.join(project, template), 'utf8');
  assert(text.includes('gameManager.onLaunchProgress((e) => {'));
  assert(text.includes('gameManager.onModulePrepared(() => {'));
}
const start = source.indexOf('        static bool InjectGameJs(');
const end = source.indexOf('\n    }\n\n    /// <summary>', start);
assert(start > 0 && end > start);
const methods = source.slice(start, end);
fs.writeFileSync(path.join(root, 'Harness.cs'), `
using System;
using System.IO;
using System.Text;
using July.Release;
namespace July.Release.Editor {
public class BuildContext { public string Platform, Env = "Prod", CoreVersion = "1.0.0", GameJsPath; }
static class PreloadHelper { public static string FindGameJs(BuildContext ctx) => ctx.GameJsPath; }
static class Debug { public static void Log(object value) {} public static void LogWarning(object value) {} public static void LogError(object value) {} }
public class PreloadInjectionStep {
const string Sentinel = "[PreloadInjection]", WeChatStartGameMarker = "gameManager.startGame();", TikTokMainMarker = "main();";
${methods}
public static void Main(string[] args) {
  foreach (var platform in new[] { "WeChat", "TikTok" }) {
    var dir = Path.Combine(args[0], platform);
    Directory.CreateDirectory(dir);
    var js = Path.Combine(dir, "game.js");
    var lifecycle = "gameManager.onLaunchProgress((e) => { GameGlobal.existingProgress = e.type; }); gameManager.onModulePrepared(() => { GameGlobal.existingPrepared = true; }); gameManager.startGame();";
    File.WriteAllText(js, platform == "WeChat" ? lifecycle : "const managerConfig = {preloadDataList: [undefined, 'old.bundle']}; function main() { " + lifecycle + " } main();");
    File.WriteAllText(Path.Combine(dir, "game.json"), "{\\"deviceOrientation\\":\\"portrait\\",\\"subpackages\\":[{\\"name\\":\\"wasm\\",\\"root\\":\\"wasmcode\\"}],\\"preloadDataList\\":[\\"existing.bundle\\"],\\"preloadDataListUrl\\":\\"https://old.example.com/preload.json\\"}");
    var ctx = new BuildContext { Platform = platform, GameJsPath = js };
    if (!InjectGameJs(ctx, "https://cdn.example.com/Game/Prod/" + platform + "/1.0.0/preload.json", "https://config.example.com", "1.0.0")) throw new Exception("Injection failed");
    var first = File.ReadAllText(js);
    var json = File.ReadAllText(Path.Combine(dir, "game.json"));
    InjectGameJs(ctx, "https://cdn.example.com/Game/Prod/" + platform + "/1.0.0/preload.json", "https://config.example.com", "1.0.0");
    if (first != File.ReadAllText(js) || json != File.ReadAllText(Path.Combine(dir, "game.json"))) throw new Exception("Repeated injection changed output");
  }
  var withoutNativeUrl = ConfigureTikTokPreload("{}");
  if (ConfigureTikTokPreload(withoutNativeUrl) != withoutNativeUrl) throw new Exception("Removing an absent native URL must be idempotent");
}
}}
`);
fs.writeFileSync(path.join(root, 'Harness.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup><Compile Include="Harness.cs" />
<Compile Include="${runtime}/ClientVersionProtocol.cs" />
<Compile Include="${runtime}/ClientVersionJson.cs" />
<Compile Include="${runtime}/PlatformKeys.cs" />
<Reference Include="LitJson"><HintPath>${litJson}</HintPath></Reference>
</ItemGroup></Project>`);
fs.writeFileSync(path.join(root, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>');
const dotnetOptions = { stdio: 'inherit', env: { ...process.env, APPDATA: root, NUGET_PACKAGES: path.join(root, 'nuget'), DOTNET_CLI_HOME: root, DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1', DOTNET_NOLOGO: '1', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: 'false' } };
execFileSync('dotnet', ['restore', path.join(root, 'Harness.csproj'), '--configfile', path.join(root, 'NuGet.Config')], dotnetOptions);
execFileSync('dotnet', ['run', '--no-restore', '--project', path.join(root, 'Harness.csproj'), '--', path.join(root, 'outputs')], dotnetOptions);


process.env.PRELOAD_TEST_OUTPUT = path.join(root, 'outputs');
await import('./test-dynamic-runtime.mjs');
