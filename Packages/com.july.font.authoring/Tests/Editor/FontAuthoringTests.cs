using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace July.Font.Authoring.Tests
{
    public sealed class FontAuthoringTests
    {
        [Test]
        public void OutputUsesSourceDirectorySuffixAndFormat()
        {
            var source = Path.Combine(Path.GetTempPath(), "字体 folder", "示例.OTF");
            Assert.That(FontSubsetRequest.OutputPath(source), Is.EqualTo(Path.Combine(Path.GetDirectoryName(source), "示例-subset.OTF")));
            Assert.That(FontSubsetRequest.CharacterOutputPath(source), Is.EqualTo(Path.Combine(Path.GetDirectoryName(source), "示例-subset.charset.txt")));
            Assert.Throws<ArgumentException>(() => FontSubsetRequest.OutputPath("example.woff2"));
        }

        [Test]
        public void ExtraCharactersCountCodepointsAndKeepShapingCharacters()
        {
            Assert.That(FontAuthoringWindow.ExtraCharacterCount("𠮷𠮷A\n\t \u200d\ufe0f\ufeff"), Is.EqualTo(5));
        }

        [Test]
        public void RequestJsonPreservesUnicodeAndOptions()
        {
            var request = new FontSubsetRequest { input = "字体 folder/示例.ttf", extraText = "𠮷\"\n中", useDefault = false, replace = true };
            var parsed = JsonUtility.FromJson<FontSubsetRequest>(JsonUtility.ToJson(request));
            Assert.That(parsed.input, Is.EqualTo(request.input));
            Assert.That(parsed.extraText, Is.EqualTo(request.extraText));
            Assert.That(parsed.useDefault, Is.False);
            Assert.That(parsed.replace, Is.True);
        }

        [Test]
        public void EditorEntryIsAvailableWithoutUiRuntimeOrTmp()
        {
            Assert.That(typeof(FontAuthoringWindow).Assembly.GetReferencedAssemblies().Select(item => item.Name)
                .Any(name => name.StartsWith("July.UI") || name.StartsWith("Unity.TextMeshPro")), Is.False);
            var menu = typeof(FontAuthoringWindow).GetMethod(nameof(FontAuthoringWindow.ShowWindow)).GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Single();
            Assert.That(menu.menuItem, Is.EqualTo("JulyGF/资源管理/字体裁剪"));
        }

    }
}
