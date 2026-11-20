using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
namespace July.Release.Editor
{
    public static class TMPFontSwapper
    {
        static int _depth;
        static UnityEngine.Object _originalFont;
        static UnityEngine.Object[] _fallbacks;
        public static IDisposable UseLaunchFont()
        {
            var initialDepth = _depth;
            SwapToLaunchFont();
            return new FontScope(initialDepth);
        }
        sealed class FontScope : IDisposable
        {
            readonly int _initialDepth;
            public FontScope(int initialDepth) => _initialDepth = initialDepth;
            public void Dispose()
            {
                while (_depth > _initialDepth) RestoreFont();
            }
        }
        internal static IDisposable WithoutStaticSourceFonts()
        {
            var scope = new StaticSourceScope();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" }))
                {
                    var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (font != null && font.atlasPopulationMode == AtlasPopulationMode.Static && font.sourceFontFile == null)
                        scope.RemoveSource(font);
                }
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        sealed class StaticSourceScope : IDisposable
        {
            const string EditorSourceField = "m_SourceFontFile_EditorRef";
            readonly List<(GlobalObjectId Font, GlobalObjectId Source)> _sources = new();

            internal void RemoveSource(TMP_FontAsset font)
            {
                using var serialized = new SerializedObject(font);
                var source = serialized.FindProperty(EditorSourceField);
                if (source.objectReferenceValue == null) return;

                _sources.Add((GlobalObjectId.GetGlobalObjectIdSlow(font),
                    GlobalObjectId.GetGlobalObjectIdSlow(source.objectReferenceValue)));
                source.objectReferenceValue = null;
                SaveSourceChange(font, serialized);
            }

            public void Dispose()
            {
                foreach (var (fontId, sourceId) in _sources)
                {
                    // Reimport can invalidate the object instances captured before the build.
                    var font = (TMP_FontAsset)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(fontId);
                    var sourceFont = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(sourceId);
                    if (font == null || sourceFont == null)
                        throw new InvalidOperationException($"无法恢复静态 TMP 字体 {fontId} 的编辑器源字体 {sourceId}。");

                    using var serialized = new SerializedObject(font);
                    serialized.FindProperty(EditorSourceField).objectReferenceValue = sourceFont;
                    SaveSourceChange(font, serialized);
                }
                _sources.Clear();
            }

            static void SaveSourceChange(TMP_FontAsset font, SerializedObject serialized)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(font);
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(font), ImportAssetOptions.ForceSynchronousImport);
            }
        }

        public static void SwapToLaunchFont()
        {
            if (_depth > 0) { _depth++; return; }
            var configuredFont = ReleaseProject.LoadBuildConfig().launchFont;
            if (configuredFont == null) return; // 未启用字体替换，保持 TMP 项目配置。
            var settings = TMP_Settings.instance;
            if (settings == null) throw new InvalidOperationException("TMP Settings is missing.");
            var font = configuredFont;
            if (font == null) throw new InvalidOperationException("The configured launch font does not exist.");
            var serialized = new SerializedObject(settings);
            var defaultFont = serialized.FindProperty("m_defaultFontAsset");
            var fallback = serialized.FindProperty("m_fallbackFontAssets");
            _originalFont = defaultFont.objectReferenceValue;
            _fallbacks = new UnityEngine.Object[fallback.arraySize];
            for (var i = 0; i < _fallbacks.Length; i++) _fallbacks[i] = fallback.GetArrayElementAtIndex(i).objectReferenceValue;
            _depth = 1;
            defaultFont.objectReferenceValue = font;
            fallback.ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings);
        }
        public static void RestoreFont()
        {
            // Postprocess and the outer finally can both release a completed scope.
            if (_depth == 0 || --_depth > 0) return;
            var settings = TMP_Settings.instance;
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = _originalFont;
            var fallback = serialized.FindProperty("m_fallbackFontAssets");
            fallback.arraySize = _fallbacks.Length;
            for (var i = 0; i < _fallbacks.Length; i++) fallback.GetArrayElementAtIndex(i).objectReferenceValue = _fallbacks[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings);
            _originalFont = null;
            _fallbacks = null;
        }
    }
}
