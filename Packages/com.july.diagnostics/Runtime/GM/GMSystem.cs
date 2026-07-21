#if JULYGF_DEBUG
using System;
using July.Arch;
using TMPro;

namespace July.Diagnostics
{
    public class GMSystem : SystemBase, IGMSystem
    {
        private readonly GMRegistry _registry = new();
        private GMOverlayRoot _overlayRoot;
        private bool _dirty;

        public void Register(Type type)
        {
            if (_registry.Register(type))
                _dirty = true;
        }

        public void Unregister(Type type)
        {
            if (_registry.Unregister(type))
                _dirty = true;
        }

        public void Build(TMP_FontAsset font = null)
        {
            if (font != null) GMUGUIPanel.OverrideFont = font;

            if (_overlayRoot != null && !_dirty) return;
            _dirty = false;

            if (_overlayRoot != null)
            {
                UnityEngine.Object.Destroy(_overlayRoot.gameObject);
                _overlayRoot = null;
            }
            _overlayRoot = GMOverlayRoot.Create(_registry.Categories);
        }

        protected override void OnShutdown()
        {
            if (_overlayRoot != null)
            {
                UnityEngine.Object.Destroy(_overlayRoot.gameObject);
                _overlayRoot = null;
            }

            _registry.Clear();
        }
    }
}
#endif
