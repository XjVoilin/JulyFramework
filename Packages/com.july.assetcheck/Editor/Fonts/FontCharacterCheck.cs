using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;

namespace July.AssetCheck
{
    internal sealed class FontTextSource
    {
        internal string AssetPath { get; }
        internal string ObjectPath { get; }
        internal AssetCheckObjectLocation Location { get; }
        internal bool LocalizedDefault { get; }
        internal string Limitation { get; }
        internal string Label => Location == null ? (AssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "JSON：" : "资源：") + AssetPath : AssetPath + " / " + ObjectPath +
            (LocalizedDefault ? "（本地化组件默认文字）" : "");

        internal FontTextSource(string path, string objectPath = null, AssetCheckObjectLocation location = null,
            bool localizedDefault = false, string limitation = null)
        { AssetPath = path; ObjectPath = objectPath; Location = location; LocalizedDefault = localizedDefault; Limitation = limitation; }
    }

    internal sealed class FontMissingCharacter
    {
        internal uint Unicode { get; }
        internal string Text => char.ConvertFromUtf32((int)Unicode);
        internal FontTextSource[] Sources { get; }
        internal string[] SourcePaths => Sources.Select(source => source.AssetPath).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
        internal FontMissingCharacter(uint unicode, IEnumerable<FontTextSource> sources)
        { Unicode = unicode; Sources = sources.Distinct().OrderBy(source => source.Label, StringComparer.Ordinal).ToArray(); }
    }

    internal sealed class FontCoverage
    {
        internal string FontName { get; }
        internal string FontPath { get; }
        internal Dictionary<uint, HashSet<FontTextSource>> Required { get; } = new();
        internal FontMissingCharacter[] Missing { get; private set; }
        private readonly HashSet<uint> _available;

        internal FontCoverage(TMP_FontAsset font)
        {
            FontName = font.name; FontPath = AssetDatabase.GetAssetPath(font);
            _available = new HashSet<uint>(font.characterTable.Select(character => character.unicode));
        }

        internal void Add(IEnumerable<uint> characters, FontTextSource source)
        {
            foreach (var character in characters)
            {
                if (!Required.TryGetValue(character, out var sources)) Required.Add(character, sources = new HashSet<FontTextSource>());
                sources.Add(source);
            }
        }

        internal void Complete() => Missing = FontCheckResult.FindMissing(Required, _available);
    }

    internal sealed class FontCheckResult
    {
        internal FontMissingCharacter[] CharacterFileMissing { get; }
        internal FontMissingCharacter[] FontMissing { get; }
        internal FontMissingCharacter[] MissingCharacters { get; }
        internal FontCoverage[] Fonts { get; }
        internal FontTextSource[] Unverified { get; }
        internal int SourceCount => JsonSourceCount + AssetSourceCount;
        internal int JsonSourceCount { get; }
        internal int AssetSourceCount { get; }
        internal int TextCount { get; }
        internal int CharacterCount { get; }
        internal string CheckedAt { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        internal FontCheckResult(Dictionary<uint, HashSet<FontTextSource>> jsonRequired, HashSet<uint> characterFile,
            IEnumerable<FontCoverage> fonts, IEnumerable<FontTextSource> unverified, int jsonCount, int assetCount, int textCount)
        {
            JsonSourceCount = jsonCount; AssetSourceCount = assetCount; TextCount = textCount;
            Fonts = fonts.OrderBy(font => font.FontPath, StringComparer.Ordinal).ToArray();
            foreach (var font in Fonts) font.Complete();
            Unverified = unverified.OrderBy(source => source.Label, StringComparer.Ordinal).ToArray();
            CharacterCount = Fonts.SelectMany(font => font.Required.Keys).Concat(jsonRequired.Keys).Distinct().Count();
            CharacterFileMissing = FindMissing(jsonRequired, characterFile);
            FontMissing = Merge(Fonts.SelectMany(font => font.Missing));
            MissingCharacters = Merge(CharacterFileMissing.Concat(FontMissing));
        }

        internal static FontMissingCharacter[] FindMissing(Dictionary<uint, HashSet<FontTextSource>> required, HashSet<uint> available) =>
            required.Where(pair => !available.Contains(pair.Key)).OrderBy(pair => pair.Key)
                .Select(pair => new FontMissingCharacter(pair.Key, pair.Value)).ToArray();

        private static FontMissingCharacter[] Merge(IEnumerable<FontMissingCharacter> entries) => entries.GroupBy(entry => entry.Unicode)
            .OrderBy(group => group.Key).Select(group => new FontMissingCharacter(group.Key, group.SelectMany(entry => entry.Sources))).ToArray();

        internal static string CopyText(IEnumerable<FontMissingCharacter> entries) => string.Concat(entries.Select(entry => entry.Text));
    }

    internal static class FontJsonCharacters
    {
        internal static HashSet<uint> Read(string path)
        {
            try { return Parse(File.ReadAllText(path)); }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException ||
                exception is IOException || exception is UnauthorizedAccessException)
            { throw new InvalidDataException("无法读取字体检查 JSON：" + path + "\n" + exception.Message, exception); }
        }

        internal static HashSet<uint> Parse(string json)
        {
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            var root = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new JsonReaderException("JSON 根节点之后存在额外内容。");
            var result = new HashSet<uint>();
            Collect(root, result);
            return result;
        }

        internal static HashSet<uint> FromText(string text)
        {
            var result = new HashSet<uint>();
            for (var index = 0; index < text.Length; index++)
            {
                var unicode = (uint)char.ConvertToUtf32(text, index);
                if (CharUnicodeInfo.GetUnicodeCategory(text, index) != UnicodeCategory.Control) result.Add(unicode);
                if (char.IsHighSurrogate(text[index])) index++;
            }
            return result;
        }

        private static void Collect(JToken token, HashSet<uint> result)
        {
            if (token is JValue value && value.Type == JTokenType.String) result.UnionWith(FromText((string)value));
            else foreach (var child in token.Children()) Collect(child, result);
        }
    }
}
