using System;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal static class TextureFormatStandardsView
    {
        internal static void Build(VisualElement parent, string label, TextureImporterFormat[] allowed,
            TextureImporterFormat target, Action<TextureImporterFormat[]> changeAllowed, Action<TextureImporterFormat> changeTarget)
        {
            var group = new VisualElement(); group.AddToClassList("format-standard");
            group.Add(new Label(label + "允许格式"));
            var choices = new VisualElement(); choices.AddToClassList("format-options");
            foreach (var format in MiniGameProfile.TextureFormats)
            {
                var toggle = new Toggle(TextureFormatRule.FormatName(format)) { value = allowed.Contains(format) };
                toggle.SetEnabled(format != target);
                if (format == target) toggle.tooltip = "修改目标必须保留在允许集合中；先选择其他修改目标后才能移除。";
                toggle.RegisterValueChangedCallback(change => changeAllowed(change.newValue
                    ? allowed.Concat(new[] { format }).ToArray() : allowed.Where(value => value != format).ToArray()));
                choices.Add(toggle);
            }
            group.Add(choices);
            AssetCheckStandardFields.AddChoice(group, label + "修改目标", allowed.ToList(), target, changeTarget);
            parent.Add(group);
        }
    }
}
