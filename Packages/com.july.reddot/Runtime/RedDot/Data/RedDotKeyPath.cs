using System;
using System.Collections.Generic;
using System.Text;

namespace July.RedDot
{
    /// <summary>
    /// Defines the single identity policy for authored red-dot nodes.
    /// Local node keys remain short; runtime keys, display paths and generated
    /// C# identifiers are derived from the complete ancestry.
    /// </summary>
    public static class RedDotKeyPath
    {
        public const string RuntimeSeparator = "/";
        public const string DisplaySeparator = " › ";

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        public static IReadOnlyList<string> GetSegments(
            RedDotTreeConfig config,
            RedDotNodeDefinition node)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (node == null) throw new ArgumentNullException(nameof(node));

            var segments = new List<string>();
            var current = node;
            var visited = new HashSet<RedDotNodeDefinition>();

            while (current != null && visited.Add(current))
            {
                if (!string.IsNullOrWhiteSpace(current.key))
                    segments.Insert(0, current.key.Trim());

                if (string.IsNullOrEmpty(current.parentKey))
                    break;

                current = config.GetNode(current.parentKey);
            }

            return segments;
        }

        public static RedDotNodeDefinition GetParent(
            RedDotTreeConfig config,
            RedDotNodeDefinition node)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (node == null) throw new ArgumentNullException(nameof(node));
            return string.IsNullOrEmpty(node.parentKey) ? null : config.GetNode(node.parentKey);
        }

        public static string GetRuntimeKey(RedDotTreeConfig config, RedDotNodeDefinition node)
            => string.Join(RuntimeSeparator, GetSegments(config, node));

        public static string GetParentRuntimeKey(RedDotTreeConfig config, RedDotNodeDefinition node)
        {
            var segments = GetSegments(config, node);
            return segments.Count <= 1
                ? null
                : string.Join(RuntimeSeparator, CopyWithoutLast(segments));
        }

        public static string GetDisplayPath(RedDotTreeConfig config, RedDotNodeDefinition node)
            => string.Join(DisplaySeparator, GetSegments(config, node));

        public static string GetCodeIdentifier(RedDotTreeConfig config, RedDotNodeDefinition node)
        {
            var segments = GetSegments(config, node);
            var sanitized = new string[segments.Count];
            for (var i = 0; i < segments.Count; i++)
                sanitized[i] = SanitizeIdentifierSegment(segments[i]);
            return string.Join("_", sanitized);
        }

        private static IEnumerable<string> CopyWithoutLast(IReadOnlyList<string> segments)
        {
            for (var i = 0; i < segments.Count - 1; i++)
                yield return segments[i];
        }

        private static string SanitizeIdentifierSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "_";

            var builder = new StringBuilder(value.Length + 1);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                var valid = i == 0
                    ? char.IsLetter(character) || character == '_'
                    : char.IsLetterOrDigit(character) || character == '_';

                if (valid)
                {
                    builder.Append(character);
                    continue;
                }

                if (i == 0 && char.IsDigit(character))
                    builder.Append('_').Append(character);
                else
                    builder.Append('_');
            }

            var identifier = builder.ToString();
            return CSharpKeywords.Contains(identifier) ? $"_{identifier}" : identifier;
        }
    }
}
