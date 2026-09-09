using System;
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
        public static void SwapToLaunchFont()
        {
            if (_depth > 0) { _depth++; return; }
            var settings = TMP_Settings.instance;
            if (settings == null) throw new InvalidOperationException("TMP Settings is missing.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AssetDatabase.GUIDToAssetPath(ReleaseProject.Profile.LaunchFontGuid));
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
