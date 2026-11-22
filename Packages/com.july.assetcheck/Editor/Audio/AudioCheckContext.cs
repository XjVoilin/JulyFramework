using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    public sealed class AudioCheckContext : AssetCheckContext
    {
        internal override bool SupportsFix => true;
        public AudioClip Clip { get; }
        public AudioStandards Standards { get; }
        internal AudioImporter Importer { get; }
        internal IReadOnlyList<AudioPlatformSettings.Platform> Platforms { get; }
        internal bool IsBgm { get; }
        internal bool IsSfx { get; }
        internal bool HasUsageConflict => IsBgm && IsSfx;
        internal AudioLoadTypes AllowedLoadTypes => IsBgm ? Standards.BgmLoadTypes : Standards.SfxLoadTypes;
        internal bool CanSetPlatformSampleRate => Standards.maxSampleRate >= AudioPlatformSettings.SupportedSampleRate;
        internal AudioImporterSampleSettings? WebGLEdit { get; private set; }
        internal AudioImporterSampleSettings? MiniGameEdit { get; private set; }
        internal bool SetMono { get; set; }

        /// <summary>读取模块已解析的平台生效设置。platformName 支持 WebGL 和 MiniGame。
        /// overridden 为 false 表示该平台继承默认设置。</summary>
        public bool TryGetPlatformSettings(string platformName, out AudioImporterSampleSettings samples, out bool overridden)
        {
            var platform = Platforms.FirstOrDefault(item => item.Name == platformName);
            samples = platform.Samples;
            overridden = platform.Overridden;
            return platform.Name != null;
        }

        internal AudioCheckContext(AudioClip clip, string path, AudioStandards standards, List<AssetCheckIssue> issues,
            AudioImporter importer, IReadOnlyList<AudioPlatformSettings.Platform> platforms)
            : base(path, AssetCheckKind.Audio, issues)
        {
            Clip = clip;
            Standards = standards;
            Importer = importer;
            Platforms = platforms;
            if (importer == null) return;
            foreach (var label in AssetDatabase.GetLabels(clip))
            {
                IsBgm |= string.Equals(label, "BGM", StringComparison.OrdinalIgnoreCase);
                IsSfx |= string.Equals(label, "SFX", StringComparison.OrdinalIgnoreCase);
            }
        }

        internal bool AllowsSampleRate(AudioImporterSampleSettings settings) =>
            settings.sampleRateSetting != AudioSampleRateSetting.OverrideSampleRate ||
            settings.sampleRateOverride <= Standards.maxSampleRate;

        internal bool AllowsLoadType(AudioClipLoadType loadType)
        {
            if (HasUsageConflict) return false;
            if (!IsBgm && !IsSfx) return true;
            return (uint)loadType <= (uint)AudioClipLoadType.Streaming &&
                (AllowedLoadTypes & (AudioLoadTypes)(1 << (int)loadType)) != 0;
        }

        internal AudioClipLoadType PreferredLoadType()
        {
            if (!IsBgm && AllowsLoadType(AudioClipLoadType.DecompressOnLoad))
                return AudioClipLoadType.DecompressOnLoad;
            if (AllowsLoadType(AudioClipLoadType.CompressedInMemory))
                return AudioClipLoadType.CompressedInMemory;
            if (AllowsLoadType(AudioClipLoadType.DecompressOnLoad))
                return AudioClipLoadType.DecompressOnLoad;
            return AudioClipLoadType.Streaming;
        }

        internal void EditPlatform(string name, Func<AudioImporterSampleSettings, AudioImporterSampleSettings> change)
        {
            var platform = Platforms.Single(item => item.Name == name);
            var pending = name == AudioPlatformSettings.WebGL ? WebGLEdit : MiniGameEdit;
            var input = pending ?? (platform.Overridden
                ? platform.Samples : AudioPlatformSettings.WithSupportedSampleRate(platform.Samples));
            var edit = change(input);
            if (name == AudioPlatformSettings.WebGL) WebGLEdit = edit;
            else MiniGameEdit = edit;
        }

        internal void SaveEdits()
        {
            if (WebGLEdit.HasValue || MiniGameEdit.HasValue || SetMono)
                AudioPlatformSettings.WriteEdits(Importer, WebGLEdit, MiniGameEdit, SetMono);
        }
    }
}
