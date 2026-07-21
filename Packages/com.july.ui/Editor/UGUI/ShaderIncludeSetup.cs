using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.UI.Editor
{
    /// <summary>
    /// 自动将 UIGrayGroup 依赖的 UI/Grayscale shader 加入 Always Included Shaders，
    /// 防止真机打包时被 shader stripping 裁剪导致 Shader.Find 返回 null。
    /// </summary>
    [InitializeOnLoad]
    static class ShaderIncludeSetup
    {
        private static readonly string[] RequiredShaders = { "UI/Grayscale" };

        static ShaderIncludeSetup()
        {
            EnsureShadersIncluded();
        }

        private static void EnsureShadersIncluded()
        {
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (!graphicsSettings) return;

            var so = new SerializedObject(graphicsSettings);
            var prop = so.FindProperty("m_AlwaysIncludedShaders");
            if (prop == null || !prop.isArray) return;

            var existing = new HashSet<string>();
            for (int i = 0; i < prop.arraySize; i++)
            {
                var shader = prop.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader) existing.Add(shader.name);
            }

            bool changed = false;
            foreach (var name in RequiredShaders)
            {
                if (existing.Contains(name)) continue;
                var shader = Shader.Find(name);
                if (!shader) continue;

                prop.InsertArrayElementAtIndex(prop.arraySize);
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[July.UI] Added required shaders to Always Included Shaders.");
            }
        }
    }
}
