using System;
using UnityEditor;

namespace July.AssetCheck
{
    // Unity 2022.3 对团结平台的 ASTC 调用 SetPlatformTextureSettings 会报错，
    // 即使启用 ignorePlatformSupport 也如此。这两个平台使用编辑器的导入器序列化数据处理，
    // 不使用反射、替换 YAML 或屏蔽错误日志。
    internal static class TuanjieTextureSettings
    {
        internal static void WriteMaxSize(TextureImporter importer, string target, int size)
        {
            var settings = importer.GetPlatformTextureSettings(target);
            if (!settings.overridden)
            {
                importer.GetDefaultPlatformTextureSettings().CopyTo(settings);
                var originalFormat = settings.format;
                settings.name = target;
                settings.overridden = true;
                settings.format = TextureImporterFormat.RGBA32;
                importer.SetPlatformTextureSettings(settings);
                using (var serialized = new SerializedObject(importer))
                {
                    var entry = RequireEntry(serialized, target);
                    entry.FindPropertyRelative("m_TextureFormat").intValue = (int)originalFormat;
                    entry.FindPropertyRelative("m_MaxTextureSize").intValue = size;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            else
            {
                using (var serialized = new SerializedObject(importer))
                {
                    RequireEntry(serialized, target).FindPropertyRelative("m_MaxTextureSize").intValue = size;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static SerializedProperty RequireEntry(SerializedObject serialized, string target)
        {
            var platforms = serialized.FindProperty("m_PlatformSettings");
            if (platforms == null)
                throw new NotSupportedException("当前编辑器的纹理导入序列化结构不受支持。");
            var index = Find(platforms, target);
            if (index < 0)
                throw new InvalidOperationException("TextureImporter 未建立目标平台设置：" + target);
            return platforms.GetArrayElementAtIndex(index);
        }
        internal static void WriteFormat(TextureImporter importer, string target, TextureImporterFormat format)
        {
            var settings = importer.GetPlatformTextureSettings(target);
            if (!settings.overridden)
            {
                importer.GetDefaultPlatformTextureSettings().CopyTo(settings);
                settings.name = target;
                settings.overridden = true;
                // 由原生导入器创建并初始化条目；Unity 2022.3 通过 SerializedProperty
                // 调整平台序列化数组大小时可能崩溃。
                settings.format = TextureImporterFormat.RGBA32;
                importer.SetPlatformTextureSettings(settings);
            }
            using (var serialized = new SerializedObject(importer))
            {
                var platforms = serialized.FindProperty("m_PlatformSettings");
                if (platforms == null)
                    throw new NotSupportedException("当前编辑器的纹理导入序列化结构不受支持，无法写入团结平台设置。");

                var index = Find(platforms, target);
                if (index < 0)
                    throw new InvalidOperationException("TextureImporter 未建立目标平台设置：" + target);

                var entry = platforms.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("m_TextureFormat").intValue = (int)format;
                entry.FindPropertyRelative("m_TextureCompression").intValue = (int)TextureImporterCompression.Compressed;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static int Find(SerializedProperty platforms, string target)
        {
            for (var index = 0; index < platforms.arraySize; index++)
                if (platforms.GetArrayElementAtIndex(index).FindPropertyRelative("m_BuildTarget").stringValue == target)
                    return index;
            return -1;
        }
    }
}
