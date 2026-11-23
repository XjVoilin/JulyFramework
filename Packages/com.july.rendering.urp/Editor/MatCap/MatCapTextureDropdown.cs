using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    internal sealed class MatCapTextureDropdown : AdvancedDropdown
    {
        internal sealed class Item : AdvancedDropdownItem
        {
            internal bool IsAssigned { get; }

            internal Item(string label, Texture2D texture, bool assigned, bool selected)
                : base(label + (assigned ? " · 材质自带" : "") + (selected ? "  ✓" : ""))
            {
                icon = texture;
                id = texture.GetInstanceID();
                IsAssigned = assigned;
            }
        }

        readonly AdvancedDropdownItem root;
        readonly Action<Texture2D, bool> onSelected;

        internal MatCapTextureDropdown(AdvancedDropdownState state, AdvancedDropdownItem root, Action<Texture2D, bool> onSelected)
            : base(state)
        {
            this.root = root;
            this.onSelected = onSelected;
            minimumSize = new Vector2(320, 300);
        }

        protected override AdvancedDropdownItem BuildRoot() => root;

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is Item textureItem) onSelected(textureItem.icon, textureItem.IsAssigned);
        }
    }
}
