using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace July.UI.Authoring.Editor.Tests
{
    public sealed class PS2UGUISettingsTests
    {
        [Test] public void SettingsPersistInProjectSettingsWithoutCreatingAssets()
        {
            const string path = "ProjectSettings/July/UIAuthoring/settings.asset";
            byte[] before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            int assets = Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories).Length;
            var settings = PS2UGUISettings.Instance;
            string old = settings.FontMaterialPath;
            try
            {
                settings.FontMaterialPath = "Assets/CustomFonts";
                typeof(ScriptableSingleton<PS2UGUISettings>).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, new object[] { true });
                Assert.That(File.Exists(path), Is.True); Assert.That(File.Exists(path + ".meta"), Is.False);
                Assert.That(File.ReadAllText(path), Does.Contain("Assets/CustomFonts"));
                Assert.That(Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories).Length, Is.EqualTo(assets));
                Assert.That(PS2UGUISettings.GetFontMaterialPath(), Is.EqualTo("Assets/CustomFonts"));
            }
            finally
            {
                settings.FontMaterialPath = old;
                if (before == null) File.Delete(path); else File.WriteAllBytes(path, before);
            }
        }
    }
}
