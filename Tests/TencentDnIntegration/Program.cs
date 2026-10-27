using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using July.Analytics.TencentDn;
using July.Analytics.TencentDn.Editor;
using July.Release;
using July.Release.Editor;
using UnityEditor.PackageManager;

internal static class Program
{
    private static int _assertions;
    private static void Main(string[] args)
    {
        PackageInfo.PackageRoot = Path.GetFullPath(args[0]);
        // 独立的新目录模拟全量导出生成的新产物，不复用上次检查的遗留文件。
        var output = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var fixture = "import './weapp-adapter';\nconst managerConfig = { APPID: 'wx-test' };\n" +
            "GameGlobal.managerConfig = managerConfig;\ncheckVersion().then(() => {\n" +
            "GameGlobal.managerConfig = managerConfig;\ngameManager.startGame();\n});\n";
        var context = new BuildContext { Platform = PlatformKeys.WeChat };
        var boot = new Boot();
        var processor = new TencentDnExportProcessor();
        Check(processor.Validate(context, boot) == null, "Disabled does not need credentials");
        boot.TencentDn.Enabled = true;
        Check(processor.Validate(context, boot) != null, "Invalid credentials rejected");
        context.Platform = PlatformKeys.TikTok;
        Check(processor.Validate(context, boot) == null, "Other platform does not validate Tencent");
        context.Platform = PlatformKeys.WeChat;
        boot.TencentDn.UserActionSetId = 123;
        boot.TencentDn.SecretKey = new string('x', 28) + "\"\n中文";
        Check(processor.Validate(context, boot) == null, "Complete config accepted");

        foreach (var template in new[]
        {
            (Source: fixture, Newline: "\n"),
            (Source: fixture.TrimEnd('\n'), Newline: "\n"),
            (Source: fixture.Replace("\n", "\r\n"), Newline: "\r\n"),
            (Source: fixture.Replace("\n", "\r\n").TrimEnd('\r', '\n'), Newline: "\r\n"),
            (Source: fixture.Replace("import './weapp-adapter';\n", "import './weapp-adapter';\r\n"), Newline: "\r\n")
        })
            CheckFreshExport(template.Source, template.Newline, output, processor, context, boot);
        if (args.Length > 2)
        {
            var actual = File.ReadAllText(args[2]).Replace("\r\n", "\n");
            CheckFreshExport(actual, "\n", output, processor, context, boot);
            CheckFreshExport(actual.Replace("\n", "\r\n"), "\r\n", output, processor, context, boot);
        }
        foreach (var invalidTemplate in new[]
        {
            "unrecognized template",
            fixture.Replace("import './weapp-adapter';", ""),
            fixture + "import './weapp-adapter';\n",
            fixture.Replace("checkVersion().then(", "checkVersionChanged().then("),
            fixture + "checkVersion().then(",
            fixture.Replace("\n", "")
        })
            CheckRejectedTemplate(invalidTemplate, output, processor, context, boot);

        var game = Path.Combine(output, "game.js");
        File.WriteAllText(game, fixture);
        var artifacts = new PlatformBuildArtifacts(game);
        var sdkDirectory = Path.Combine(output, "july-tencent-dn");
        // 停用及非微信只跳过处理，新产物不应创建 SDK 子目录或改写 game.js。
        boot.TencentDn.Enabled = false;
        processor.Process(context, boot, artifacts);
        Check(File.ReadAllText(game) == fixture && !Directory.Exists(sdkDirectory), "Disabled leaves fresh export unchanged");
        boot.TencentDn.Enabled = true;
        context.Platform = PlatformKeys.TikTok;
        processor.Process(context, boot, artifacts);
        Check(File.ReadAllText(game) == fixture && !Directory.Exists(sdkDirectory), "Other platform leaves fresh export unchanged");
        context.Platform = PlatformKeys.WeChat;

        var registry = new ReleaseExportProcessorRegistry();
        var calls = new List<string>();
        registry.Register(new Probe("b", calls));
        registry.Register(new Probe("a", calls));
        Check(registry.Validate(context, boot) == null, "Registry validates processors");
        registry.Process(context, boot, artifacts);
        Check(calls.SequenceEqual(new[] { "validate:a", "validate:b", "process:a", "process:b" }), "Stable processor order");
        Reject(() => registry.Register(new Probe("a", calls)));
        var invalid = new ReleaseExportProcessorRegistry();
        invalid.Register(new Probe("fail", calls, error: "invalid"));
        Check(invalid.Validate(context, boot) == "fail: invalid", "Validation failure propagated");
        var failed = new ReleaseExportProcessorRegistry();
        failed.Register(new Probe("fail", calls, throwOnProcess: true));
        Reject(() => failed.Process(context, boot, artifacts));
        Console.WriteLine($"PASS: export/registry contracts, {_assertions} assertions.");
    }

    private static int Count(string value, string token) => (value.Length - value.Replace(token, "").Length) / token.Length;
    private static void CheckFreshExport(string source, string newline, string output,
        TencentDnExportProcessor processor, BuildContext context, Boot boot)
    {
        var directory = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var game = Path.Combine(directory, "game.js");
        File.WriteAllText(game, source);
        processor.Process(context, boot, new PlatformBuildArtifacts(game));
        var content = File.ReadAllText(game);
        const string import = "import { initializeTencentDn } from './july-tencent-dn/bootstrap.js';";
        const string initialization = "initializeTencentDn(Object.assign";
        Check(Count(content, import) == 1 && Count(content, initialization) == 1, "Exactly one import and initialization");
        Check(content.IndexOf(import, StringComparison.Ordinal) > content.IndexOf("import './weapp-adapter';", StringComparison.Ordinal),
            "SDK import follows adapter");
        var start = content.IndexOf(initialization, StringComparison.Ordinal);
        Check(start > content.IndexOf("const managerConfig", StringComparison.Ordinal) &&
            start < content.IndexOf("checkVersion().then(", StringComparison.Ordinal), "Initialization follows config and precedes startup");
        Check(Count(content, "gameManager.startGame();") == Count(source, "gameManager.startGame();"), "Preload anchor preserved");
        var end = content.IndexOf('\n', start) + 1;
        var line = content.Substring(start, end - start);
        Check((line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n") == newline &&
            content.Contains(import + newline), "Injected lines follow template line endings");
        // 只移除已确认的两行新增代码，验证其余原始文本与末尾换行完全保留。
        Check(content.Remove(start, end - start).Replace(import + newline, "") == source, "Original template text unchanged");
        Check(content.Contains("\\\"") && content.Contains("\\n"), "Configuration uses JSON escaping");
        var sdkDirectory = Path.Combine(directory, "july-tencent-dn");
        Check(Directory.GetFiles(sdkDirectory).Select(Path.GetFileName).OrderBy(name => name)
            .SequenceEqual(new[] { "bootstrap.js", "dn-sdk-minigame.js" }), "Output contains only SDK and bootstrap");
        Check(File.ReadAllBytes(Path.Combine(sdkDirectory, "dn-sdk-minigame.js")).SequenceEqual(File.ReadAllBytes(
            Path.Combine(PackageInfo.PackageRoot, "SDK~", "dn-sdk-minigame.js"))), "Vendor copied unchanged");
    }

    private static void CheckRejectedTemplate(string source, string output,
        TencentDnExportProcessor processor, BuildContext context, Boot boot)
    {
        var directory = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var game = Path.Combine(directory, "game.js");
        File.WriteAllText(game, source);
        Reject(() => processor.Process(context, boot, new PlatformBuildArtifacts(game)));
        Check(File.ReadAllText(game) == source && !Directory.Exists(Path.Combine(directory, "july-tencent-dn")),
            "Invalid template fails before changing artifacts");
    }
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        var rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Expected explicit failure");
    }
    private sealed class Boot : IReleaseBootConfig, ITencentDnConfigProvider
    {
        public TencentDnConfig TencentDn { get; } = new();
    }
    private sealed class Probe : IReleaseExportProcessor
    {
        public string Name { get; }
        private readonly List<string> _calls;
        private readonly string _error;
        private readonly bool _throwOnProcess;
        public Probe(string name, List<string> calls, string error = null, bool throwOnProcess = false)
        { Name = name; _calls = calls; _error = error; _throwOnProcess = throwOnProcess; }
        public string Validate(BuildContext context, IReleaseBootConfig bootConfig)
        { _calls.Add("validate:" + Name); return _error; }
        public void Process(BuildContext context, IReleaseBootConfig bootConfig, PlatformBuildArtifacts artifacts)
        {
            if (_throwOnProcess) throw new InvalidDataException("Fixture processing failure");
            _calls.Add("process:" + Name);
        }
    }
}
