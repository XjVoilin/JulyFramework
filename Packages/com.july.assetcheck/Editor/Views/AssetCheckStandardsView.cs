using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal static class AssetCheckStandardsView
    {
        internal static VisualElement Build(AssetCheckKind kind, AssetCheckSettings settings, Action<Action<AssetCheckSettings>> change)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 190;
            scroll.style.height = 110;
            var line = new VisualElement();
            line.AddToClassList("standard-fields");
            scroll.Add(line);
            AssetCheckCatalog.Entries.Single(entry => entry.Kind == kind).Form(line, settings.CurrentStandards,
                action => change(value => action(value.CurrentStandards)));
            return scroll;
        }
    }

    internal static class AssetCheckStandardFields
    {
        internal static void Integrity(VisualElement line, PlatformStandards standards, Action<Action<PlatformStandards>> edit)
            => line.Add(new Label("完整性检查无可调参数：正常未赋值不报错。"));
        internal static void AddFormat(VisualElement parent, string label, TextureImporterFormat value, Action<TextureImporterFormat> change)
        {
            var field = new PopupField<TextureImporterFormat>(label, MiniGameProfile.TextureFormats, value,
                TextureFormatRule.FormatName, TextureFormatRule.FormatName);
            field.RegisterValueChangedCallback(e => change(e.newValue));
            Add(parent, field);
        }
        internal static void AddPermission(VisualElement parent, string label, bool allowed, Action<bool> change) =>
            AddChoice(parent, label, new List<string> { "禁止", "允许" }, allowed ? "允许" : "禁止", v => change(v == "允许"));
        internal static void AddRequired(VisualElement parent, string label, bool required, Action<bool> change) =>
            AddChoice(parent, label, new List<string> { "必须开启", "不限制" }, required ? "必须开启" : "不限制", v => change(v == "必须开启"));
        internal static void AddSize(VisualElement parent, string label, int value, Action<int> change) =>
            AddChoice(parent, label, new List<int> { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 }, value, change);
        internal static void AddChoice<T>(VisualElement parent, string label, List<T> choices, T value, Action<T> change)
        {
            var field = new PopupField<T>(label, choices, value);
            field.RegisterValueChangedCallback(e => change(e.newValue));
            Add(parent, field);
        }
        internal static void AddInt(VisualElement parent, string label, int value, Action<int> change)
        {
            var field = new IntegerField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(e => change(e.newValue));
            Add(parent, field);
        }
        internal static void AddFloat(VisualElement parent, string label, float value, Action<float> change)
        {
            var field = new FloatField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(e => change(e.newValue));
            Add(parent, field);
        }
        internal static void Add(VisualElement parent, VisualElement field)
        {
            field.AddToClassList("standard-field");
            parent.Add(field);
        }
    }
}
