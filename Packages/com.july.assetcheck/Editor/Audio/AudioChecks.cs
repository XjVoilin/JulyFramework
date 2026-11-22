using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class AudioSampleRateRule : IAssetCheckFix<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            var clip = context.Clip;
            var path = context.AssetPath;
            var standards = context.Standards;
            if (clip.frequency <= standards.maxSampleRate) return;

            var activePlatform = AudioPlatformSettings.ActivePlatform;
            var importedPlatform = context.Platforms.FirstOrDefault(platform => platform.Name == activePlatform);
            if (importedPlatform.Name != null &&
                importedPlatform.Samples.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate &&
                importedPlatform.Samples.sampleRateOverride > standards.maxSampleRate)
                return; // 固定采样率规则已报告这个平台。
            if (activePlatform == null && context.Platforms.Count > 0 &&
                !context.Platforms.Any(platform => platform.Samples.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate))
                return;

            var targets = activePlatform == null ? Array.Empty<string>() : new[] { activePlatform };
            var canFix = context.CanSetPlatformSampleRate && importedPlatform.Name != null &&
                (importedPlatform.Samples.sampleRateSetting != AudioSampleRateSetting.OverrideSampleRate ||
                 importedPlatform.Samples.sampleRateOverride != AudioPlatformSettings.SupportedSampleRate);
            context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Audio,
                "audio.sample-rate", "导入采样率", clip.frequency + " Hz", "≤ " + standards.maxSampleRate + " Hz",
                canFix, targets: targets));
        }

        public void Fix(AudioCheckContext context, AssetCheckIssue issue) =>
            context.EditPlatform(issue.Targets.Single(), AudioPlatformSettings.WithSupportedSampleRate);
    }

    internal sealed class AudioChannelsRule : IAssetCheckFix<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            var clip = context.Clip;
            var path = context.AssetPath;
            var standards = context.Standards;
            if (clip.channels <= standards.maxChannels) return;
            var canFix = standards.maxChannels == 1 && context.Importer != null && !context.Importer.forceToMono;
            context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Audio,
                "audio.channels", "导入声道数", clip.channels.ToString(), canFix ? "单声道（所有平台）" : "≤ " + standards.maxChannels, canFix));
        }

        public void Fix(AudioCheckContext context, AssetCheckIssue issue) => context.SetMono = true;
    }

    internal sealed class AudioMiniGameOverrideRule : IAssetCheckFix<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            if (!context.Standards.requireMiniGameOverride) return;
            var miniGame = context.Platforms.FirstOrDefault(platform => platform.Name == AudioPlatformSettings.MiniGame);
            if (miniGame.Name == null || miniGame.Overridden) return;

            var canFix = context.CanSetPlatformSampleRate;
            var expected = canFix ? "按音频标准启用 MiniGame 覆盖" :
                $"MiniGame 需要 {AudioPlatformSettings.SupportedSampleRate} Hz，采样率上限需支持该值";
            context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath),
                AssetCheckKind.Audio, "audio.platform-override", "小游戏平台覆盖",
                "继承 Default", expected, canFix, targets: new[] { miniGame.Name }));
        }

        public void Fix(AudioCheckContext context, AssetCheckIssue issue) =>
            context.EditPlatform(AudioPlatformSettings.MiniGame, settings =>
            {
                if (!context.HasUsageConflict && !context.AllowsLoadType(settings.loadType))
                    settings.loadType = context.PreferredLoadType();
                return settings;
            });
    }

    internal sealed class AudioPlatformSampleRateRule : IAssetCheckFix<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            foreach (var platform in context.Platforms)
            {
                var settings = platform.Samples;
                if (context.AllowsSampleRate(settings)) continue;
                context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath),
                    AssetCheckKind.Audio, "audio.platform-sample-rate", "平台固定采样率",
                    settings.sampleRateOverride + " Hz", "≤ " + context.Standards.maxSampleRate + " Hz",
                    context.CanSetPlatformSampleRate, targets: new[] { platform.Name }, location: platform.Name));
            }
        }

        public void Fix(AudioCheckContext context, AssetCheckIssue issue) =>
            context.EditPlatform(issue.Targets.Single(), AudioPlatformSettings.WithSupportedSampleRate);
    }

    internal sealed class AudioPlatformLoadTypeRule : IAssetCheckFix<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            if (!context.IsBgm && !context.IsSfx) return;
            if (context.HasUsageConflict)
            {
                context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath),
                    AssetCheckKind.Audio, "audio.usage", "音频用途", "BGM / SFX", "选择一种用途标签", false));
                return;
            }
            foreach (var platform in context.Platforms)
            {
                var current = platform.Samples.loadType;
                if (context.AllowsLoadType(current)) continue;
                context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath),
                    AssetCheckKind.Audio, "audio.platform-load-type", context.IsBgm ? "BGM 加载方式" : "SFX 加载方式",
                    current.ToString(), context.AllowedLoadTypes.ToString(), platform.Overridden || context.CanSetPlatformSampleRate,
                    targets: new[] { platform.Name }, location: platform.Name));
            }
        }

        public void Fix(AudioCheckContext context, AssetCheckIssue issue) =>
            context.EditPlatform(issue.Targets.Single(), settings =>
            {
                settings.loadType = context.PreferredLoadType();
                return settings;
            });
    }

    internal sealed class AudioCheckModule : IAssetCheckModule
    {
        internal static readonly AssetCheckRegistration Registration = new AssetCheckRegistration(
            AssetCheckKind.Audio, () => new AudioCheckModule(), "音频",
            "检查采样率、声道和小游戏平台设置。按当前音频标准修复所选音频的可修改问题。",
            standards => standards.audio, AudioStandardsView.Build, fixAllSelectedIssues: true, fitStandardsToContent: true);

        private readonly IAssetCheckRule<AudioCheckContext>[] _rules = AssetCheckRules<AudioCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.Audio;
        public string RulesFingerprint => AssetCheckRules<AudioCheckContext>.Fingerprint;
        public string[] Collect(string scope) => AssetDatabase.FindAssets("t:AudioClip", new[] { scope });
        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var issues = new List<AssetCheckIssue>();
            AssetCheckRules<AudioCheckContext>.Check(_rules, CreateContext(assetPath, standards.audio, issues));
            return issues;
        }

        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards)
        {
            var context = CreateContext(assetPath, standards.audio, new List<AssetCheckIssue>());
            foreach (var issue in issues) AssetCheckRules<AudioCheckContext>.Fix(context, issue);
            context.SaveEdits();
        }

        private static AudioCheckContext CreateContext(string assetPath, AudioStandards standards, List<AssetCheckIssue> issues)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            var platforms = importer != null
                ? new[] { AudioPlatformSettings.ReadWebGL(importer), AudioPlatformSettings.ReadMiniGame(importer) }
                : Array.Empty<AudioPlatformSettings.Platform>();
            return new AudioCheckContext(clip, assetPath, standards, issues, importer, platforms);
        }
    }
}
