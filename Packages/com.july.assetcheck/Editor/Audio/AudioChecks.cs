using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class AudioSampleRateRule : IAssetCheckRule<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            var clip = context.Clip;
            var path = context.AssetPath;
            var standards = context.Standards;
            if (clip.frequency > standards.maxSampleRate)
                context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Audio,
                    "audio.sample-rate", "导入采样率", clip.frequency + " Hz", "≤ " + standards.maxSampleRate + " Hz", false));
        }
    }

    internal sealed class AudioChannelsRule : IAssetCheckRule<AudioCheckContext>
    {
        public void Check(AudioCheckContext context)
        {
            var clip = context.Clip;
            var path = context.AssetPath;
            var standards = context.Standards;
            if (clip.channels > standards.maxChannels)
                context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Audio,
                    "audio.channels", "导入声道数", clip.channels.ToString(), "≤ " + standards.maxChannels, false));
        }
    }

    internal sealed class AudioCheckModule : IAssetCheckModule
    {
        private readonly IAssetCheckRule<AudioCheckContext>[] _rules = AssetCheckRules<AudioCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.Audio;
        public string RulesFingerprint => AssetCheckRules<AudioCheckContext>.Fingerprint;
        public string[] Collect(string scope) => AssetDatabase.FindAssets("t:AudioClip", new[] { scope });
        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            var issues = new List<AssetCheckIssue>();
            AssetCheckRules<AudioCheckContext>.Check(_rules, new AudioCheckContext(clip, assetPath, standards.audio, issues));
            return issues;
        }
        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards) =>
            throw new InvalidOperationException("第一版音频检查只定位问题，采样率与声道由项目人工处理。");
    }
}
