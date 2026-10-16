using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    internal static class SerializedReferenceChecks
    {
        internal static void Check(Object owner, AssetCheckContext context, string location = "", Func<SerializedProperty, bool> include = null, long localId = 0, Func<SerializedProperty, string> displayField = null)
        {
            var path = context.AssetPath;
            using (var serialized = new SerializedObject(owner))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.propertyPath == "m_Script" ||
                        (owner is Material && property.propertyPath == "m_Shader") ||
                        (include != null && !include(property)))
                        continue;
                    if (property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0)
                        continue;
                    var label = displayField != null ? displayField(property) : ReadableField(property.propertyPath);
                    var field = location + " / " + label;
                    context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), context.Kind, "reference.missing",
                        "失效引用", "已保存的引用无法解析", "有效引用或正常未赋值", false,
                        resourceName: path + " / " + field.TrimStart(' ', '/'), location: field, localId: localId, propertyPath: property.propertyPath)
                    { ObjectLocation = AssetCheckObjectLocation.Capture(owner) });
                }
            }
        }
        internal static string ReadableField(string path)
        {
            var value = path.Replace(".Array.data", "").Replace(".m_", ".");
            return value.StartsWith("m_", StringComparison.Ordinal) ? value.Substring(2) : value.TrimStart('_');
        }
    }
}
