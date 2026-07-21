using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace July.UI
{
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class WebImage : MonoBehaviour
    {
        private static readonly Dictionary<string, Sprite> _cache = new();

        private Image _image;
        private string _currentUrl;
        private CancellationTokenSource _cts;

        public string CurrentUrl => _currentUrl;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        public void Load(string url)
        {
            if (url == _currentUrl) return;

            Cancel();
            _currentUrl = url;

            if (string.IsNullOrEmpty(url))
            {
                _image.overrideSprite = null;
                return;
            }

            if (_cache.TryGetValue(url, out var cached) && cached != null)
            {
                _image.overrideSprite = cached;
                return;
            }

            _cts = new CancellationTokenSource();
            LoadAsync(url, _cts).Forget();
        }

        public void Clear()
        {
            Cancel();
            _currentUrl = null;
            _image.overrideSprite = null;
        }

        public static void ClearCache()
        {
            foreach (var sprite in _cache.Values)
            {
                if (sprite == null) continue;
                var texture = sprite.texture;
                Destroy(sprite);
                if (texture != null) Destroy(texture);
            }

            _cache.Clear();
        }

        private async UniTaskVoid LoadAsync(string url, CancellationTokenSource cts)
        {
            using var request = UnityWebRequestTexture.GetTexture(url);

            try
            {
                await request.SendWebRequest().WithCancellation(cts.Token);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[WebImage] Load failed ({url}): {request.error}");
                return;
            }

            if (this == null) return;

            if (_cache.TryGetValue(url, out var existing) && existing != null)
            {
                if (_currentUrl == url)
                    _image.overrideSprite = existing;
                return;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);

            _cache[url] = sprite;

            if (_currentUrl == url)
                _image.overrideSprite = sprite;
        }

        private void Cancel()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        private void OnDestroy()
        {
            Cancel();
        }
    }
}
