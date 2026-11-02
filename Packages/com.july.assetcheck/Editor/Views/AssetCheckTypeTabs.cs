using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class AssetCheckTypeTabs : VisualElement
    {
        private readonly Dictionary<AssetCheckKind, Button> _buttons = new Dictionary<AssetCheckKind, Button>();
        private readonly Dictionary<AssetCheckKind, Label> _counts = new Dictionary<AssetCheckKind, Label>();

        internal AssetCheckTypeTabs(Action<AssetCheckKind> select, string prefix = "tab-")
        {
            AddToClassList("type-tabs");
            style.flexDirection = FlexDirection.Row; style.flexShrink = 0; style.height = 40;
            foreach (var registration in AssetCheckCatalog.Entries)
            {
                var kind = registration.Kind;
                var button = new Button(() => select(kind)) { name = prefix + kind };
                button.AddToClassList("type-tab");
                var title = new Label(registration.Name); title.AddToClassList("tab-title"); button.Add(title);
                var count = new Label("—"); count.AddToClassList("tab-count"); button.Add(count);
                _buttons.Add(kind, button); _counts.Add(kind, count); Add(button);
            }
        }

        internal void Select(AssetCheckKind? kind)
        {
            foreach (var pair in _buttons) pair.Value.EnableInClassList("active", pair.Key == kind);
        }

        internal void Bind(AssetCheckKind kind, string count, string tooltip, bool zero, bool available = true)
        {
            _counts[kind].text = count; _counts[kind].EnableInClassList("zero", zero);
            _buttons[kind].tooltip = tooltip; _buttons[kind].SetEnabled(available);
        }
    }
}
