using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// UI 灰度组：对子节点 Image 等 Graphic 使用灰度 shader，对 TMP 直接改色。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIGrayGroup : MonoBehaviour
    {
        private const string GrayscaleShaderName = "UI/Grayscale";

        [SerializeField] private bool _gray;
        [SerializeField] private Color _tmpGrayColor = new(0.267f, 0.267f, 0.267f, 1f);

        private static Material _sharedGrayMat;

        private Graphic[] _graphics;
        private Material[] _originalMaterials;
        private Color[] _originalTmpColors;
        private bool _initialized;

        public bool IsGray => _gray;

        private void Awake()
        {
            EnsureSharedMaterial();
            CacheGraphics();
            if (_gray) ApplyGray();
        }

        public void SetGray(bool value)
        {
            if (_gray == value && _initialized) return;
            _gray = value;

            if (!_initialized) CacheGraphics();

            if (_gray) ApplyGray();
            else Restore();
        }

        public void Refresh()
        {
            CacheGraphics();
            if (_gray) ApplyGray();
        }

        private static void EnsureSharedMaterial()
        {
            if (_sharedGrayMat) return;
            var shader = Shader.Find(GrayscaleShaderName);
            if (!shader) return;
            _sharedGrayMat = new Material(shader) { name = "UIGrayscale (Shared)" };
        }

        private void CacheGraphics()
        {
            _graphics = GetComponentsInChildren<Graphic>(true);
            _originalMaterials = new Material[_graphics.Length];
            _originalTmpColors = new Color[_graphics.Length];

            for (var i = 0; i < _graphics.Length; i++)
            {
                if (!_graphics[i]) continue;
                _originalMaterials[i] = _graphics[i].material;

                if (_graphics[i] is TMP_Text tmp)
                    _originalTmpColors[i] = tmp.color;
            }

            _initialized = true;
        }

        private void ApplyGray()
        {
            if (!_initialized) return;

            for (var i = 0; i < _graphics.Length; i++)
            {
                var g = _graphics[i];
                if (!g) continue;

                if (g is TMP_Text tmp)
                {
                    tmp.color = _tmpGrayColor;
                }
                else if (_sharedGrayMat)
                {
                    g.material = _sharedGrayMat;
                }
            }
        }

        private void Restore()
        {
            if (!_initialized) return;

            for (var i = 0; i < _graphics.Length; i++)
            {
                var g = _graphics[i];
                if (!g) continue;

                if (g is TMP_Text tmp)
                    tmp.color = _originalTmpColors[i];
                else
                    g.material = _originalMaterials[i];
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureSharedMaterial();
            if (!_initialized) CacheGraphics();

            if (_gray) ApplyGray();
            else Restore();
        }
#endif
    }
}
