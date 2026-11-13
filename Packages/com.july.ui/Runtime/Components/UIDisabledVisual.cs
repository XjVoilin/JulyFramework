using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>按明确配置切换正常／不可用表现，不拥有业务条件或输入许可。</summary>
    [DisallowMultipleComponent]
    public sealed class UIDisabledVisual : MonoBehaviour
    {
        [Serializable]
        private sealed class SpriteBinding
        {
            public Image _target;
            public Sprite _normal;
            public Sprite _disabled;
        }

        [Serializable]
        private sealed class ActiveBinding
        {
            public GameObject _target;
            public bool _normal = true;
            public bool _disabled;
        }

        [Serializable]
        private sealed class ColorBinding
        {
            public Graphic _target;
            public Color _normal = Color.white;
            public Color _disabled = Color.gray;
        }

        [Serializable]
        private sealed class MaterialBinding
        {
            public TMP_Text _target;
            public Material _normal;
            public Material _disabled;
        }

        [Tooltip("Initial state for standalone use. A bound UISmartButton supplies the state.")]
        [SerializeField] private bool _disabled;
        [SerializeField] private SpriteBinding[] _sprites = Array.Empty<SpriteBinding>();
        [SerializeField] private ActiveBinding[] _activeObjects = Array.Empty<ActiveBinding>();
        [SerializeField] private ColorBinding[] _colors = Array.Empty<ColorBinding>();
        [SerializeField] private MaterialBinding[] _textMaterials = Array.Empty<MaterialBinding>();

        private bool _validated;

        public bool IsDisabled => _disabled;

        private void OnEnable() => SetDisabled(_disabled);

        /// <summary>重复设置也会重新应用；关闭窗口不会自动恢复为正常态。</summary>
        public void SetDisabled(bool disabled)
        {
            if (!_validated)
            {
                var errors = new List<string>();
                CollectConfigurationErrors(errors);
                if (errors.Count > 0)
                    throw new InvalidOperationException($"{name}: Invalid UIDisabledVisual configuration:\n" + string.Join("\n", errors));
                _validated = true;
            }

            _disabled = disabled;
            ApplyState(disabled);
        }

        internal void ApplyState(bool disabled)
        {
            // 先应用内容与样式，再显示节点，避免 OnEnable 观察到旧样式。
            foreach (var binding in _sprites)
                binding._target.sprite = disabled ? binding._disabled : binding._normal;
            foreach (var binding in _colors)
                binding._target.color = disabled ? binding._disabled : binding._normal;
            foreach (var binding in _textMaterials)
                binding._target.fontSharedMaterial = disabled ? binding._disabled : binding._normal;
            foreach (var binding in _activeObjects)
                binding._target.SetActive(disabled ? binding._disabled : binding._normal);
        }

        internal void CollectConfigurationErrors(List<string> errors)
        {
            var properties = new HashSet<(UnityEngine.Object, string)>();
            for (var i = 0; i < _sprites.Length; i++)
            {
                var binding = _sprites[i];
                ValidateTarget(binding._target, "sprite", $"Sprites[{i}]", properties, errors);
                // 空 Sprite 是显式清空图片，允许配置。
            }
            for (var i = 0; i < _activeObjects.Length; i++)
            {
                var binding = _activeObjects[i];
                var label = $"Visibility[{i}]";
                ValidateTarget(binding._target, "activeSelf", label, properties, errors);
                if (binding._target && transform.IsChildOf(binding._target.transform))
                    errors.Add($"{label}: cannot control this GameObject or an ancestor.");
            }
            for (var i = 0; i < _colors.Length; i++)
            {
                var binding = _colors[i];
                ValidateTarget(binding._target, "color", $"Colors[{i}]", properties, errors);
            }
            for (var i = 0; i < _textMaterials.Length; i++)
            {
                var binding = _textMaterials[i];
                var label = $"Text Materials[{i}]";
                ValidateTarget(binding._target, "fontSharedMaterial", label, properties, errors);
                if (!binding._target) continue;
                ValidateTextMaterial(binding._target, binding._normal, label + " Normal", errors);
                ValidateTextMaterial(binding._target, binding._disabled, label + " Disabled", errors);
            }
        }

        private void ValidateTarget(UnityEngine.Object target, string property, string label,
            HashSet<(UnityEngine.Object, string)> properties, List<string> errors)
        {
            if (!target)
            {
                errors.Add($"{label}: missing target.");
                return;
            }

            var targetTransform = target is GameObject go ? go.transform : ((Component)target).transform;
            if (!targetTransform.IsChildOf(transform))
                errors.Add($"{label}: the target must be on this GameObject or a descendant.");
            if (!properties.Add((target, property)))
                errors.Add($"{label}: duplicate binding for {target.name}.{property}.");
        }

        private static void ValidateTextMaterial(TMP_Text text, Material material, string label, List<string> errors)
        {
            if (!text.font || !material || !material.HasProperty(ShaderUtilities.ID_MainTex)
                || !material.HasProperty(ShaderUtilities.ID_GradientScale)
                || material.GetTexture(ShaderUtilities.ID_MainTex) != text.font.atlasTexture)
                errors.Add($"{label}: requires a TMP SDF material preset using the target font's atlas texture.");
        }
    }
}
