using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal static class AudioPlatformSettings
    {
        internal const string WebGL = "WebGL";
        internal const string MiniGame = "MiniGame";
        internal const uint SupportedSampleRate = 44100;

        internal static AudioImporterSampleSettings WithSupportedSampleRate(AudioImporterSampleSettings samples)
        {
            // 当前 Unity WebGL 与团结 MiniGame 的 AAC 导入接口接受明确的 44.1 kHz。
            samples.compressionFormat = AudioCompressionFormat.AAC;
            samples.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            samples.sampleRateOverride = SupportedSampleRate;
            return samples;
        }

        internal static string ActivePlatform
        {
            get
            {
                var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                if (group == BuildTargetGroup.WebGL) return WebGL;
#if TUANJIE_1_5_OR_NEWER
                if (group == BuildTargetGroup.MiniGame) return MiniGame;
#endif
                return null;
            }
        }

        internal readonly struct Platform
        {
            internal string Name { get; }
            internal AudioImporterSampleSettings Samples { get; }
            internal bool Overridden { get; }

            internal Platform(string name, AudioImporterSampleSettings samples, bool overridden)
            {
                Name = name;
                Samples = samples;
                Overridden = overridden;
            }

            internal string Summary => $"{Samples.compressionFormat} / {Samples.loadType}";
            internal string Details
            {
                get
                {
                    var rate = Samples.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate
                        ? $"{Samples.sampleRateOverride} Hz"
                        : Samples.sampleRateSetting.ToString();
                    return $"{Summary}\n采样率：{rate}\n质量：{Samples.quality:P0}\n预加载音频数据：{Samples.preloadAudioData}";
                }
            }
        }

        internal static Platform ReadWebGL(AudioImporter importer) =>
            new Platform(WebGL, importer.GetOverrideSampleSettings(BuildTargetGroup.WebGL),
                importer.ContainsSampleSettingsOverride(BuildTargetGroup.WebGL));

        internal static Platform ReadMiniGame(AudioImporter importer)
        {
#if TUANJIE_1_5_OR_NEWER
            return new Platform(MiniGame, importer.GetOverrideSampleSettings(BuildTargetGroup.MiniGame),
                importer.ContainsSampleSettingsOverride(BuildTargetGroup.MiniGame));
#else
            var text = File.ReadAllText(MetaPath(importer.assetPath));
            var entry = ReadEntry(text, MiniGameEntry);
            return entry.Success
                ? new Platform(MiniGame, ReadStoredSamples(entry.Groups["settings"].Value), true)
                : new Platform(MiniGame, importer.defaultSampleSettings, false);
#endif
        }

        internal static void CopyWebGLToMiniGame(AudioImporter importer)
        {
#if TUANJIE_1_5_OR_NEWER
            WriteEdits(importer, null, ReadWebGL(importer).Samples, false);
#else
            var text = File.ReadAllText(importer.assetPath + ".meta");
            var source = ReadEntry(text, WebGLEntry);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var settings = source.Success ? source.Groups["settings"].Value : StoreSamples(ReadWebGL(importer).Samples, newline);
            var updated = ReplaceOverride(text, ReadEntry(text, MiniGameEntry), "    36:" + newline + settings, newline);
            WriteMeta(importer, text, updated);
#endif
        }

        internal static void WriteEdits(AudioImporter importer, AudioImporterSampleSettings? webGL,
            AudioImporterSampleSettings? miniGame, bool forceMono)
        {
#if TUANJIE_1_5_OR_NEWER
            if (webGL.HasValue) SetOverride(importer, BuildTargetGroup.WebGL, webGL.Value);
            if (miniGame.HasValue) SetOverride(importer, BuildTargetGroup.MiniGame, miniGame.Value);
            if (forceMono) importer.forceToMono = true;
            importer.SaveAndReimport();
#else
            // 一次提交两份覆盖，避免普通 Unity 保存 WebGL 时重新解释 MiniGame 的 36 号键。
            var text = File.ReadAllText(importer.assetPath + ".meta");
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var updated = text;
            if (webGL.HasValue) updated = EditOverride(updated, WebGLEntry, 13, webGL.Value, newline);
            if (miniGame.HasValue) updated = EditOverride(updated, MiniGameEntry, 36, miniGame.Value, newline);
            if (forceMono)
            {
                var flag = ForceToMonoEntry.Match(updated);
                if (!flag.Success) throw new InvalidDataException("无法识别音频导入器的单声道设置。");
                updated = updated.Remove(flag.Index, flag.Length).Insert(flag.Index, "  forceToMono: 1");
            }
            WriteMeta(importer, text, updated);
#endif
        }

#if TUANJIE_1_5_OR_NEWER
        private static void SetOverride(AudioImporter importer, BuildTargetGroup target, AudioImporterSampleSettings samples)
        {
            if (!importer.SetOverrideSampleSettings(target, samples))
                throw new InvalidOperationException(target + " 拒绝此音频的平台设置：" + importer.assetPath);
        }
#endif

#if !TUANJIE_1_5_OR_NEWER
        private static void WriteMeta(AudioImporter importer, string previous, string updated)
        {
            if (updated == previous) return;
            var metaPath = importer.assetPath + ".meta";
            var temporaryPath = metaPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, updated);
                AssetCheckFileReplacement.Commit(temporaryPath, metaPath);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
        }

        // 普通 Unity 将 36 解释成 VisionOS 并转换音频设置；小游戏项目的 MiniGame 覆盖以磁盘上的值为准。
        private static readonly Regex WebGLEntry = Entry(13);
        private static readonly Regex MiniGameEntry = Entry(36);
        private static readonly Regex OverrideSection = new Regex(
            @"^  platformSettingOverrides:\r?\n(?<entries>(?:^    [^\r\n]+\r?\n)*)", RegexOptions.Multiline);
        private static readonly Regex EmptyOverrideSection = new Regex(
            @"^  platformSettingOverrides: \{\}\r?\n", RegexOptions.Multiline);
        private static readonly Regex ForceToMonoEntry = new Regex(@"^  forceToMono: [01](?=\r?$)", RegexOptions.Multiline);
        private static readonly Regex SampleField = new Regex(@"^      (?<key>\w+): [^\r\n]+", RegexOptions.Multiline);

        private static Regex Entry(int id) =>
            new Regex($@"^    {id}:\r?\n(?<settings>(?:^      [^\r\n]+\r?\n)+)", RegexOptions.Multiline);

        private static Match ReadEntry(string text, Regex entry)
        {
            var section = OverrideSection.Match(text).Groups["entries"];
            return entry.Match(text, section.Index, section.Length);
        }

        private static string EditOverride(string text, Regex entry, int id,
            AudioImporterSampleSettings samples, string newline)
        {
            var previous = ReadEntry(text, entry);
            var fields = StoreSamples(samples, newline, previous.Success ? previous.Groups["settings"].Value : null);
            return ReplaceOverride(text, previous, $"    {id}:" + newline + fields, newline);
        }

        private static string ReplaceOverride(string text, Match target, string entry, string newline)
        {
            if (target.Success) return text.Remove(target.Index, target.Length).Insert(target.Index, entry);
            var section = OverrideSection.Match(text);
            if (section.Success) return text.Insert(section.Index + section.Length, entry);
            var empty = EmptyOverrideSection.Match(text);
            if (!empty.Success) throw new InvalidDataException("无法识别音频导入器的平台覆盖结构。");
            return text.Remove(empty.Index, empty.Length).Insert(empty.Index, "  platformSettingOverrides:" + newline + entry);
        }

        private static string StoreSamples(AudioImporterSampleSettings settings, string newline, string previous = null)
        {
            var values = new Dictionary<string, string>
            {
                ["loadType"] = ((int)settings.loadType).ToString(CultureInfo.InvariantCulture),
                ["sampleRateSetting"] = ((int)settings.sampleRateSetting).ToString(CultureInfo.InvariantCulture),
                ["sampleRateOverride"] = settings.sampleRateOverride.ToString(CultureInfo.InvariantCulture),
                ["compressionFormat"] = ((int)settings.compressionFormat).ToString(CultureInfo.InvariantCulture),
                ["quality"] = settings.quality.ToString("R", CultureInfo.InvariantCulture),
                ["preloadAudioData"] = settings.preloadAudioData ? "1" : "0"
            };
            if (previous != null)
                return SampleField.Replace(previous, match =>
                {
                    var key = match.Groups["key"].Value;
                    return values.TryGetValue(key, out var value) ? $"      {key}: {value}" : match.Value;
                });
            return "      serializedVersion: 2" + newline + "      conversionMode: 0" + newline +
                string.Join(newline, values.Select(pair => $"      {pair.Key}: {pair.Value}")) + newline;
        }

        private static string MetaPath(string path)
        {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return path + ".meta";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            return Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1)) + ".meta";
        }

        private static AudioImporterSampleSettings ReadStoredSamples(string settings)
        {
            string Value(string field)
            {
                var match = Regex.Match(settings, $@"^      {field}: (?<value>[^\r\n]+)", RegexOptions.Multiline);
                if (!match.Success) throw new InvalidDataException("MiniGame 音频覆盖缺少字段 " + field);
                return match.Groups["value"].Value;
            }

            int ValueAsInt(string field) => int.Parse(Value(field), CultureInfo.InvariantCulture);
            return new AudioImporterSampleSettings
            {
                loadType = (AudioClipLoadType)ValueAsInt("loadType"),
                sampleRateSetting = (AudioSampleRateSetting)ValueAsInt("sampleRateSetting"),
                sampleRateOverride = uint.Parse(Value("sampleRateOverride"), CultureInfo.InvariantCulture),
                compressionFormat = (AudioCompressionFormat)ValueAsInt("compressionFormat"),
                quality = float.Parse(Value("quality"), CultureInfo.InvariantCulture),
                preloadAudioData = ValueAsInt("preloadAudioData") != 0
            };
        }
#endif
    }
}
