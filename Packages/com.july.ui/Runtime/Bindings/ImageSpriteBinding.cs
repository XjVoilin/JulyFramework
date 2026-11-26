using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>将资源地址绑定到一张 Image。列表项回收或持有者销毁时调用 Clear。</summary>
    public sealed class ImageSpriteBinding : ICanGetSystem
    {
        private readonly Image _image;
        private string _address;
        private CancellationTokenSource _pending;

        public ImageSpriteBinding(Image image)
        {
            _image = image;
        }

        /// <summary>显示指定图片；空地址清空图片。同一地址的加载与显示结果可复用。</summary>
        public void SetAddress(string address)
        {
            if (!string.IsNullOrEmpty(address) && address == _address &&
                (_pending != null || _image.sprite != null)) return;

            Clear();
            if (string.IsNullOrEmpty(address)) return;

            _address = address;
            _pending = new CancellationTokenSource();
            LoadAsync(address, _pending).Forget();
        }

        /// <summary>取消等待并隐藏图片。已成功加载的资源由目标 GameObject 持有。</summary>
        public void Clear()
        {
            _address = null;
            var pending = _pending;
            _pending = null;
            pending?.Cancel();

            // 持有者的 OnDestroy 也可能晚于目标 Image 的销毁。
            if (_image == null) return;
            _image.enabled = false;
            _image.sprite = null;
        }

        private async UniTask LoadAsync(string address, CancellationTokenSource request)
        {
            try
            {
                var resources = this.GetSystem<IResourceSystem>();
                var sprite = await resources.LoadAsync<Sprite>(address, _image.gameObject, request.Token);
                if (request.IsCancellationRequested) return;
                if (sprite == null) throw new ResourceException($"无法加载图片：{address}");

                _image.sprite = sprite;
                _image.enabled = true;
            }
            finally
            {
                if (_pending == request) _pending = null;
                request.Dispose();
            }
        }
    }
}
