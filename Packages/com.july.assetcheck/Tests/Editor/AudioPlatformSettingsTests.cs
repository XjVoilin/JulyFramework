using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace July.AssetCheck.Tests
{
    internal sealed class PublicAudioPlatformFactsRule : IAssetCheckRule<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            if (Path.GetFileName(context.AssetPath) != "extension-platform-facts.wav") return;
            var webGl = context.TryGetPlatformSettings("WebGL", out var source, out var webGlOverridden);
            var miniGame = context.TryGetPlatformSettings("MiniGame", out var target, out var miniGameOverridden);
            var unknown = context.TryGetPlatformSettings("Unknown", out _, out _);
            context.Report(context.Clip, "facts", "平台导入设置",
                (webGl && miniGame && webGlOverridden && !miniGameOverridden && !unknown && !source.Equals(target)).ToString(),
                "True");
        }
    }

    public sealed class AudioPlatformSettingsTests
    {
        private string _folder, _config;

        [SetUp]
        public void Setup()
        {
            var name = "__AudioPlatforms_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
            _config = Path.GetFullPath("Temp/" + name);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_folder);
            if (Directory.Exists(_config)) Directory.Delete(_config, true);
        }

        [Test]
        public void DiscoveredAudioRuleCanReadPlatformFactsThroughThePublicContext()
        {
            var path = CreateClip("extension-platform-facts", "SFX");
            var issue = new AudioCheckModule().Check(path, new PlatformStandards())
                .Single(item => item.RuleId.EndsWith("PublicAudioPlatformFactsRule.facts", StringComparison.Ordinal));
            Assert.That(issue.Actual, Is.EqualTo("True"));
        }

        [Test]
        public void CopyUsesExplicitWebGLSettingsAndPreservesSource()
        {
            var path = CreateClip("copy", "BGM");
            var defaultPath = CreateClip("default");
            var importer = Importer(path);
            var source = AudioPlatformSettings.ReadWebGL(importer).Samples;
            var defaults = importer.defaultSampleSettings;
            var guid = AssetDatabase.AssetPathToGUID(path);
            var audio = File.ReadAllBytes(path);
            var meta = File.ReadAllBytes(path + ".meta");

            Assert.That(WebGLPlatformSettingsCopy.Collect(new[] { _folder }), Is.EquivalentTo(new[] { path, defaultPath }));
            Assert.That(WebGLPlatformSettingsCopy.Inspect(path).NeedsCopy, Is.True);
            Assert.That(WebGLPlatformSettingsCopy.Inspect(defaultPath).NeedsCopy, Is.True);
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(meta));
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.True);

            importer = Importer(path);
            var target = AudioPlatformSettings.ReadMiniGame(importer);
            Assert.That(target.Overridden, Is.True);
            Assert.That(target.Samples, Is.EqualTo(source));
            Assert.That(AudioPlatformSettings.ReadWebGL(importer).Samples, Is.EqualTo(source));
            Assert.That(importer.defaultSampleSettings, Is.EqualTo(defaults));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(audio));
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.False);
        }

        [Test]
        public void CheckAllowsDifferentPlatformsWhenBothMeetTheStandard()
        {
            var path = CreateClip("effects", "SFX");
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.True);
            ChangeMiniGame(path, 32000, AudioClipLoadType.DecompressOnLoad);
            var checks = new AudioCheckModule();
            var standards = new PlatformStandards();

            Assert.That(WebGLPlatformSettingsCopy.Inspect(path).NeedsCopy, Is.True);
            Assert.That(checks.Check(path, standards), Is.Empty);

            ChangeMiniGame(path, 48000, AudioClipLoadType.DecompressOnLoad);
            var issue = checks.Check(path, standards).Single();
            Assert.That(issue.RuleId, Is.EqualTo("audio.platform-sample-rate"));
            Assert.That(issue.Targets, Is.EqualTo(new[] { "MiniGame" }));
            Assert.That(issue.Actual, Is.EqualTo("48000 Hz"));
            Assert.That(issue.CanFix, Is.True);
            checks.Fix(path, new[] { issue }, standards);
            Assert.That(AudioPlatformSettings.ReadMiniGame(Importer(path)).Samples.sampleRateOverride, Is.EqualTo(44100));
            Assert.That(checks.Check(path, standards), Is.Empty);
        }

        [Test]
        public void CheckAppliesTheBgmLoadingPolicyOnlyToExplicitBgmAssets()
        {
            var path = CreateClip("music", "BGM");
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.True);
            ChangeMiniGame(path, 44100, AudioClipLoadType.DecompressOnLoad);
            var checks = new AudioCheckModule();
            var standards = new PlatformStandards();
            var issue = checks.Check(path, standards).Single();
            Assert.That(issue.RuleId, Is.EqualTo("audio.platform-load-type"));
            Assert.That(issue.Targets, Is.EqualTo(new[] { "MiniGame" }));

            standards.audio.bgmLoadTypes = AudioLoadTypes.CompressedInMemory | AudioLoadTypes.DecompressOnLoad;
            Assert.That(checks.Check(path, standards), Is.Empty);
            var uncategorized = CreateClip("unlabeled", label: "");
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(uncategorized), Is.True);
            ChangeMiniGame(uncategorized, 44100, AudioClipLoadType.DecompressOnLoad);
            Assert.That(checks.Check(uncategorized, new PlatformStandards()), Is.Empty);
        }

        [Test]
        public void FixedMiniGameRatesTakePrecedenceOverAnUnrelatedEditorImport()
        {
            var path = CreateClip("resampled", "BGM", 48000);
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.True);
            var checks = new AudioCheckModule();
            Assert.That(checks.Check(path, new PlatformStandards()), Is.Empty);
        }

        private string CreateClip(string name, string label = null, int sourceRate = 44100, int channels = 1)
        {
            var path = _folder + "/" + name + ".wav";
            var length = sourceRate / 10 * channels * 2;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(length + 36);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(sourceRate);
                writer.Write(sourceRate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(length);
                writer.Write(new byte[length]);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (label == null) return path;

            var importer = Importer(path);
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.AAC;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 44100;
            Assert.That(importer.SetOverrideSampleSettings(BuildTargetGroup.WebGL, settings), Is.True);
            importer.SaveAndReimport();
            if (label.Length > 0) AssetDatabase.SetLabels(AssetDatabase.LoadAssetAtPath<AudioClip>(path), new[] { label });
            return path;
        }

        private static AudioImporter Importer(string path) => (AudioImporter)AssetImporter.GetAtPath(path);

        [Test]
        public void RequiresExplicitMiniGameCoverageAndCanCopyInheritedWebGLSettings()
        {
            var clip = CreateClip("inherited");
            var defaults = Importer(clip).defaultSampleSettings;
            var web = AudioPlatformSettings.ReadWebGL(Importer(clip)).Samples;
            var project = new PlatformStandards();
            project.audio.requireMiniGameOverride = true;
            var checks = new AudioCheckModule();
            Assert.That(checks.Check(clip, project).Any(issue =>
                issue.RuleId == "audio.platform-override" && issue.Targets.SequenceEqual(new[] { "MiniGame" })), Is.True);
            Assert.That(WebGLPlatformSettingsCopy.Collect(new[] { _folder }), Does.Contain(clip));
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(clip), Is.True);
            Assert.That(checks.Check(clip, project), Is.Empty);
            Assert.That(AudioPlatformSettings.ReadMiniGame(Importer(clip)).Samples, Is.EqualTo(web));
            Assert.That(AudioPlatformSettings.ReadWebGL(Importer(clip)).Overridden, Is.False);
            Assert.That(Importer(clip).defaultSampleSettings, Is.EqualTo(defaults));
        }

        [Test]
        public void ResourceCheckCanFixOnlyTheSelectedMissingMiniGameOverride()
        {
            var selected = CreateClip("selected");
            var other = CreateClip("other");
            var source = File.ReadAllBytes(selected);
            var web = AudioPlatformSettings.ReadWebGL(Importer(selected));
            var inherited = AudioPlatformSettings.ReadMiniGame(Importer(selected)).Samples;
            using var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings =>
            {
                settings.scope = _folder;
                settings.CurrentStandards.audio.requireMiniGameOverride = true;
            });
            workspace.StartScan(AssetCheckKind.Audio);
            while (workspace.Running) workspace.Step();

            var issue = workspace.GetReport(AssetCheckKind.Audio).Issues.Single(item => item.AssetPath == selected);
            Assert.That(issue.ConfirmedFix(), Is.Not.Null);
            workspace.StartFix(AssetCheckKind.Audio, new[] { issue });
            while (workspace.Running) workspace.Step();

            Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
            Assert.That(workspace.LastFix.Rechecked, Is.True);
            Assert.That(workspace.GetReport(AssetCheckKind.Audio).Issues.Select(item => item.AssetPath),
                Is.EquivalentTo(new[] { other }));
            var samples = AudioPlatformSettings.ReadMiniGame(Importer(selected)).Samples;
            Assert.That(samples.loadType, Is.EqualTo(inherited.loadType));
            Assert.That(samples.compressionFormat, Is.EqualTo(AudioCompressionFormat.AAC));
            Assert.That(samples.sampleRateOverride, Is.EqualTo(44100));
            Assert.That(AudioPlatformSettings.ReadWebGL(Importer(selected)).Samples, Is.EqualTo(web.Samples));
            Assert.That(AudioPlatformSettings.ReadWebGL(Importer(selected)).Overridden, Is.EqualTo(web.Overridden));
            Assert.That(File.ReadAllBytes(selected), Is.EqualTo(source));
        }

        [Test]
        public void MissingMiniGameOverrideCanBeFixedIndependentlyOfInvalidWebGL()
        {
            var clip = CreateClip("incompatible", "SFX");
            var importer = Importer(clip);
            var web = AudioPlatformSettings.ReadWebGL(importer);
            var defaults = importer.defaultSampleSettings;
            defaults.loadType = AudioClipLoadType.DecompressOnLoad;
            defaults.quality = 0.65f;
            importer.defaultSampleSettings = defaults;
            importer.SaveAndReimport();

            var standards = new PlatformStandards();
            standards.audio.requireMiniGameOverride = true;
            standards.audio.sfxLoadTypes = AudioLoadTypes.DecompressOnLoad;
            var checks = new AudioCheckModule();
            var inherited = AudioPlatformSettings.ReadMiniGame(Importer(clip)).Samples;
            var missing = checks.Check(clip, standards).Single(issue => issue.RuleId == "audio.platform-override");
            Assert.That(missing.CanFix, Is.True);
            checks.Fix(clip, new[] { missing }, standards);

            importer = Importer(clip);
            var miniGame = AudioPlatformSettings.ReadMiniGame(importer);
            Assert.That(miniGame.Overridden, Is.True);
            Assert.That(miniGame.Samples.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            Assert.That(miniGame.Samples.quality, Is.EqualTo(inherited.quality));
            Assert.That(AudioPlatformSettings.ReadWebGL(importer).Samples, Is.EqualTo(web.Samples));
            var remaining = checks.Check(clip, standards);
            Assert.That(remaining.Any(issue => issue.RuleId == "audio.platform-override"), Is.False);
            Assert.That(remaining.Any(issue => issue.RuleId == "audio.platform-load-type" &&
                issue.Targets.SequenceEqual(new[] { "WebGL" })), Is.True);
        }

        [Test]
        public void MissingMiniGameOverrideReportsAnIncompatibleTargetSampleRateStandard()
        {
            var clip = CreateClip("unsupported-target");
            var standards = new PlatformStandards();
            standards.audio.requireMiniGameOverride = true;
            standards.audio.maxSampleRate = 32000;
            var issue = new AudioCheckModule().Check(clip, standards).Single(item => item.RuleId == "audio.platform-override");
            Assert.That(issue.CanFix, Is.False);
            Assert.That(issue.Expected, Does.Contain("44100 Hz"));
        }

        [Test]
        public void ModifySelectedRepairsBothPlatformsEvenWhenTheIssueListIsFiltered()
        {
            var selected = CreateClip("both-platforms", "SFX");
            var other = CreateClip("other", "SFX");
            var source = File.ReadAllBytes(selected);
            using var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings =>
            {
                settings.scope = _folder;
                settings.CurrentStandards.audio.requireMiniGameOverride = true;
                settings.CurrentStandards.audio.sfxLoadTypes = AudioLoadTypes.DecompressOnLoad;
            });
            workspace.StartScan(AssetCheckKind.Audio);
            while (workspace.Running) workspace.Step();

            var model = new AssetCheckResultsModel();
            model.Bind(workspace.GetReport(AssetCheckKind.Audio));
            model.Filter("audio.platform-override");
            model.Selected.Add(AssetDatabase.AssetPathToGUID(selected));
            var fixes = model.Fixable(true);
            Assert.That(fixes.Select(issue => issue.RuleId),
                Is.EquivalentTo(new[] { "audio.platform-override", "audio.platform-load-type" }));

            workspace.StartFix(AssetCheckKind.Audio, fixes);
            while (workspace.Running) workspace.Step();

            Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
            Assert.That(workspace.GetReport(AssetCheckKind.Audio).Issues.Select(issue => issue.AssetPath).Distinct(),
                Is.EquivalentTo(new[] { other }));
            Assert.That(AudioPlatformSettings.ReadWebGL(Importer(selected)).Samples.loadType,
                Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            Assert.That(AudioPlatformSettings.ReadMiniGame(Importer(selected)).Overridden, Is.True);
            Assert.That(File.ReadAllBytes(selected), Is.EqualTo(source));
        }

#if !TUANJIE_1_5_OR_NEWER
        [Test]
        public void LockedAudioMetaReportsTheAssetAndKeepsTheOriginalSettings()
        {
            Assume.That(Application.platform, Is.EqualTo(RuntimePlatform.WindowsEditor));
            var path = CreateClip("locked");
            var meta = path + ".meta";
            var original = File.ReadAllBytes(meta);
            using var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings =>
            {
                settings.scope = _folder;
                settings.CurrentStandards.audio.requireMiniGameOverride = true;
            });
            workspace.StartScan(AssetCheckKind.Audio);
            while (workspace.Running) workspace.Step();
            var issues = workspace.GetReport(AssetCheckKind.Audio).Issues;

            using (var reader = new FileStream(meta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                LogAssert.Expect(LogType.Error, new Regex("^处理资源失败：" + Regex.Escape(path)));
                workspace.StartFix(AssetCheckKind.Audio, issues);
                while (workspace.Running) workspace.Step();
                Assert.That(workspace.Failure, Does.Contain(path));
                Assert.That(workspace.GetReport(AssetCheckKind.Audio).Failure, Does.Contain(path));
                var reportPath = new AssetCheckReportStore(_config).PathFor(AssetCheckKind.Audio);
                var saved = JsonUtility.FromJson<AssetCheckReportSnapshot>(File.ReadAllText(reportPath));
                Assert.That(saved.failure, Does.Contain(path));
                Assert.That(workspace.LastFix.ModifiedFiles, Is.Zero);
                Assert.That(File.ReadAllBytes(meta), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
            }

            workspace.StartScan(AssetCheckKind.Audio);
            while (workspace.Running) workspace.Step();
            workspace.StartFix(AssetCheckKind.Audio, workspace.GetReport(AssetCheckKind.Audio).Issues);
            while (workspace.Running) workspace.Step();
            Assert.That(workspace.LastFix.Rechecked, Is.True);
            Assert.That(workspace.GetReport(AssetCheckKind.Audio).Issues, Is.Empty);
        }
#endif

        [Test]
        public void AudioFixCombinesPlatformEditsAndMonoWithoutChangingTheSourceFile()
        {
            var path = CreateClip("combined", "SFX", channels: 2);
            var source = File.ReadAllBytes(path);
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.True);
            ChangeMiniGame(path, 48000, AudioClipLoadType.DecompressOnLoad);
            var standards = new PlatformStandards();
            standards.audio.maxChannels = 1;
            standards.audio.sfxLoadTypes = AudioLoadTypes.DecompressOnLoad;
            var checks = new AudioCheckModule();
            var fixes = checks.Check(path, standards).Where(issue => issue.CanFix).ToArray();

            Assert.That(fixes.Select(issue => issue.RuleId),
                Is.EquivalentTo(new[] { "audio.channels", "audio.platform-sample-rate", "audio.platform-load-type" }));
            checks.Fix(path, fixes, standards);

            Assert.That(Importer(path).forceToMono, Is.True);
            Assert.That(AudioPlatformSettings.ReadWebGL(Importer(path)).Samples.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            Assert.That(AudioPlatformSettings.ReadMiniGame(Importer(path)).Samples.sampleRateOverride, Is.EqualTo(44100));
            Assert.That(checks.Check(path, standards), Is.Empty);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(source));
        }

        [Test]
        public void WebGLFixKeepsADifferentCompliantMiniGameOverride()
        {
            var path = CreateClip("different", "SFX");
            Assert.That(WebGLPlatformSettingsCopy.CopyAsset(path), Is.True);
            ChangeMiniGame(path, 44100, AudioClipLoadType.DecompressOnLoad);
            var miniGame = AudioPlatformSettings.ReadMiniGame(Importer(path)).Samples;
            var standards = new PlatformStandards();
            standards.audio.sfxLoadTypes = AudioLoadTypes.DecompressOnLoad;
            var checks = new AudioCheckModule();
            var issue = checks.Check(path, standards).Single();
            Assert.That(issue.Targets, Is.EqualTo(new[] { "WebGL" }));
            Assert.That(issue.CanFix, Is.True);

            checks.Fix(path, new[] { issue }, standards);

            Assert.That(AudioPlatformSettings.ReadMiniGame(Importer(path)).Samples, Is.EqualTo(miniGame));
            Assert.That(checks.Check(path, standards), Is.Empty);
        }

        [Test]
        public void MissingMiniGameOverrideUsesTheBgmLoadTypeStandard()
        {
            var clip = CreateClip("bgm-policy", "BGM");
            var importer = Importer(clip);
            var defaults = importer.defaultSampleSettings;
            defaults.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = defaults;
            importer.SaveAndReimport();
            var standards = new PlatformStandards();
            standards.audio.requireMiniGameOverride = true;
            var checks = new AudioCheckModule();
            var missing = checks.Check(clip, standards).Single(issue => issue.RuleId == "audio.platform-override");

            checks.Fix(clip, new[] { missing }, standards);

            Assert.That(AudioPlatformSettings.ReadMiniGame(Importer(clip)).Samples.loadType,
                Is.EqualTo(AudioClipLoadType.CompressedInMemory));
            Assert.That(checks.Check(clip, standards), Is.Empty);
        }

        [Test]
        public void AudioFilterLimitsCopyAllToAudio()
        {
            var audio = CreateClip("filtered", "SFX");
            var texture = _folder + "/untouched.png";
            var image = new Texture2D(4, 4);
            try { File.WriteAllBytes(texture, image.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            AssetDatabase.ImportAsset(texture, ImportAssetOptions.ForceSynchronousImport);
            using var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings => settings.scope = _folder);
            using var panel = new WebGLPlatformSettingsCopyPanel(() => workspace, () => { });
            panel.Q<DropdownField>("resource-kind").index = 2;
            panel.StartScan();
            while (panel.Running) panel.Pump();
            var rows = panel.Q<MultiColumnListView>("results").itemsSource;
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(((WebGLPlatformSettingsCopyEntry)rows[0]).Path, Is.EqualTo(audio));

            panel.StartCopy(false);
            while (panel.Running) panel.Pump();
            Assert.That(WebGLPlatformSettingsCopy.Inspect(audio).NeedsCopy, Is.False);
            Assert.That(WebGLPlatformSettingsCopy.Inspect(texture).NeedsCopy, Is.True);
        }

        private static void ChangeMiniGame(string path, int rate, AudioClipLoadType loadType)
        {
            var metaPath = path + ".meta";
            var text = File.ReadAllText(metaPath);
            var changed = Regex.Replace(text, @"(?m)^    36:\r?\n(?:^      [^\r\n]+\r?\n)+", match =>
                Regex.Replace(Regex.Replace(match.Value, @"loadType: \d+", "loadType: " + (int)loadType),
                    @"sampleRateOverride: \d+", "sampleRateOverride: " + rate));
            Assert.That(changed, Is.Not.EqualTo(text));
            File.WriteAllText(metaPath, changed);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
