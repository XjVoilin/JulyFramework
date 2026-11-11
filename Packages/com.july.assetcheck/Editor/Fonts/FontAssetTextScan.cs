using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.AssetCheck
{
    internal sealed class FontAssetText
    {
        internal FontTextSource Source { get; }
        internal TMP_FontAsset Font { get; }
        internal HashSet<uint> Characters { get; }
        internal FontAssetText(FontTextSource source, TMP_FontAsset font, HashSet<uint> characters)
        { Source = source; Font = font; Characters = characters; }
    }

    internal static class FontAssetTextScan
    {
        internal static FontAssetText[] Read(string path, TMP_FontAsset defaultFont)
        {
            // 沿用 July 的附加场景读取流程；本次打开的内容在取消/异常时也必须释放。
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var existing = SceneManager.GetSceneByPath(path);
                var owned = !existing.IsValid() || !existing.isLoaded;
                if (!owned && existing.isDirty)
                {
                    return new[] { Unverified(path, "场景有未保存修改，请保存后重新检查。") };
                }
                var active = SceneManager.GetActiveScene();
                var selection = Selection.objects;
                var scene = owned ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : existing;
                try
                {
                    if (owned) { SceneManager.SetActiveScene(active); Selection.objects = selection; }
                    return ReadRoots(scene.GetRootGameObjects(), path, defaultFont).ToArray();
                }
                finally
                {
                    if (owned && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, !existing.IsValid());
                }
            }
            else
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.assetPath == path && stage.scene.isDirty)
                {
                    return new[] { Unverified(path, "Prefab 模式有未保存修改，请保存后重新检查。") };
                }
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    return ReadRoots(new[] { root }, path, defaultFont).ToArray();
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        private static FontAssetText Unverified(string path, string reason) =>
            new(new FontTextSource(path, limitation: reason), null, new HashSet<uint>());

        private static IEnumerable<FontAssetText> ReadRoots(GameObject[] roots, string path, TMP_FontAsset defaultFont)
        {
            foreach (var root in roots)
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // 不调用 TMP 的排版、GetTextInfo 或补字函数，避免修改动态字体字符表。
                using var serialized = new SerializedObject(text);
                var content = serialized.FindProperty("m_text").stringValue;
                if (string.IsNullOrEmpty(content)) continue;
                var fontProperty = serialized.FindProperty("m_fontAsset");
                var font = (TMP_FontAsset)fontProperty.objectReferenceValue;
                var missingFontReference = font == null && fontProperty.objectReferenceInstanceIDValue != 0;
                if (font == null && !missingFontReference) font = defaultFont;
                var characters = FontSerializedText.Characters(content,
                    serialized.FindProperty("m_isRichText").boolValue,
                    serialized.FindProperty("m_parseCtrlCharacters").boolValue, out var limitation);
                var style = (FontStyles)serialized.FindProperty("m_fontStyle").intValue;
                if ((style & (FontStyles.UpperCase | FontStyles.LowerCase | FontStyles.SmallCaps)) != 0)
                    limitation = Join(limitation, "组件包含大小写变换，未验证变换后的字形。");
                var textStyle = serialized.FindProperty("m_TextStyleHashCode").intValue;
                if (textStyle != 0 && textStyle != -1183493901) // TMP 的 Normal 样式 hash。
                    limitation = Join(limitation, "组件使用自定义文字样式，样式展开未验证。");
                if (font == null) limitation = Join(limitation, missingFontReference
                    ? "组件字体引用已失效。" : "组件未指定字体，TMP 默认字体也未设置。");
                var localized = text.GetComponents<Component>().Any(component => component != null &&
                    component.GetType().FullName == "July.Localization.UILocalizedText");
                var location = AssetCheckObjectLocation.Capture(text);
                // Prefab 的预览场景可能有其它根对象；定位时根始终是 prefabContentsRoot。
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) location.Hierarchy[0] = 0;
                var source = new FontTextSource(path, HierarchyPath(text.transform) + " / " + text.GetType().Name,
                    location, localized, limitation);
                yield return new FontAssetText(source, font, characters);
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            var parts = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                parts.Push(current.name + "[" + current.GetSiblingIndex() + "]");
            return string.Join("/", parts);
        }

        private static string Join(string first, string second) => string.IsNullOrEmpty(first) ? second : first + " " + second;
    }

    internal static class FontSerializedText
    {
        private static readonly Regex Tag = new(@"\G<(?<close>/)?(?<name>[a-z][a-z-]*)(?<args>(?:=[^<>]+| [^<>]+)?)>|\G<#[0-9a-f]{3,8}>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Formatting = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "i", "u", "s", "color", "alpha", "mark", "size", "voffset", "cspace", "mspace", "align",
            "pos", "space", "indent", "line-indent", "margin", "margin-left", "margin-right", "line-height",
            "width", "nobr", "link", "a", "page", "rotate", "scale"
        };
        private static readonly HashSet<string> Unresolved = new(StringComparer.OrdinalIgnoreCase)
        { "font", "font-weight", "style", "gradient", "lowercase", "uppercase", "allcaps", "smallcaps" };

        internal static HashSet<uint> Characters(string text, bool richText, bool parseEscapes, out string limitation)
        {
            limitation = null;
            var visible = new StringBuilder();
            var noParse = false;
            for (var index = 0; index < text.Length; index++)
            {
                if (richText && text[index] == '<')
                {
                    var tag = Tag.Match(text, index);
                    if (tag.Success)
                    {
                        var name = tag.Groups["name"].Value.ToLowerInvariant();
                        var closing = tag.Groups["close"].Success;
                        if (noParse)
                        {
                            if (name == "noparse" && closing) { noParse = false; index += tag.Length - 1; continue; }
                        }
                        else if (name == "noparse") { noParse = !closing; index += tag.Length - 1; continue; }
                        else if (Formatting.Contains(name) || name.Length == 0 || name == "sprite" || name == "br" ||
                            name == "nbsp" || name == "zwsp" || name == "shy" || Unresolved.Contains(name))
                        {
                            if (Unresolved.Contains(name)) limitation = "包含字体、样式或大小写富文本标签，相关切换未验证。";
                            if (!closing)
                            {
                                if (name == "nbsp") visible.Append('\u00a0');
                                if (name == "zwsp") visible.Append('\u200b');
                                if (name == "shy") visible.Append('\u00ad');
                            }
                            index += tag.Length - 1;
                            continue;
                        }
                    }
                    // 未识别/不完整标签保留为普通文字，避免把真正显示的尖括号内容直接丢弃。
                }
                if (text[index] == '\\' && index + 1 < text.Length)
                {
                    var escaped = text[index + 1];
                    if (parseEscapes && escaped == '\\' && index + 2 < text.Length)
                    { visible.Append('\\').Append(text[index + 2]); index += 2; continue; }
                    if (parseEscapes && (escaped == 'n' || escaped == 'r' || escaped == 't' || escaped == 'v')) { index++; continue; }
                    var digits = escaped == 'u' ? 4 : escaped == 'U' ? 8 : 0;
                    if (digits > 0 && index + 2 + digits <= text.Length && uint.TryParse(text.Substring(index + 2, digits),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicode) && unicode <= 0x10ffff &&
                        (unicode < 0xd800 || unicode > 0xdfff))
                    {
                        visible.Append(char.ConvertFromUtf32((int)unicode)); index += digits + 1; continue;
                    }
                }
                visible.Append(text[index]);
            }
            return FontJsonCharacters.FromText(visible.ToString());
        }
    }
}
