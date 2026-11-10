using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace July.AssetCheck
{
    internal sealed class FontMissingCharacter
    {
        internal uint Unicode { get; }
        internal string Text => char.ConvertFromUtf32((int)Unicode);
        internal string[] SourcePaths { get; }
        internal FontMissingCharacter(uint unicode, IEnumerable<string> paths)
        { Unicode = unicode; SourcePaths = paths.OrderBy(path => path, StringComparer.Ordinal).ToArray(); }
    }

    internal sealed class FontCheckResult
    {
        internal FontMissingCharacter[] CharacterFileMissing { get; }
        internal FontMissingCharacter[] FontMissing { get; }
        internal FontMissingCharacter[] MissingCharacters { get; }
        internal int SourceCount { get; }
        internal int CharacterCount { get; }
        internal string CheckedAt { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        internal FontCheckResult(Dictionary<uint, HashSet<string>> required, HashSet<uint> characterFile,
            HashSet<uint> font, int sourceCount)
        {
            SourceCount = sourceCount; CharacterCount = required.Count;
            CharacterFileMissing = Missing(required, characterFile);
            FontMissing = Missing(required, font);
            MissingCharacters = CharacterFileMissing.Concat(FontMissing).GroupBy(entry => entry.Unicode)
                .Select(group => group.First()).OrderBy(entry => entry.Unicode).ToArray();
        }

        private static FontMissingCharacter[] Missing(Dictionary<uint, HashSet<string>> required, HashSet<uint> available) =>
            required.Where(pair => !available.Contains(pair.Key)).OrderBy(pair => pair.Key)
                .Select(pair => new FontMissingCharacter(pair.Key, pair.Value)).ToArray();

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

        private static void Collect(JToken token, HashSet<uint> result)
        {
            if (token is JValue value && value.Type == JTokenType.String)
            {
                var text = (string)value;
                for (var index = 0; index < text.Length; index++)
                {
                    // 按 Unicode 码点去重，保留补充平面字符；换行、制表等控制符不需要字形。
                    var unicode = (uint)char.ConvertToUtf32(text, index);
                    if (CharUnicodeInfo.GetUnicodeCategory(text, index) != UnicodeCategory.Control) result.Add(unicode);
                    if (char.IsHighSurrogate(text[index])) index++;
                }
            }
            else
                foreach (var child in token.Children()) Collect(child, result);
        }
    }
}
