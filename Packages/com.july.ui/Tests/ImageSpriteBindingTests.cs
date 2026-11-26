using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI.Tests
{
    public sealed class ImageSpriteBindingTests
    {
        // 故意允许已取消的请求成功，模拟完成通知晚于列表项换绑。
        private sealed class DelayedSprites : UnityResourceSystem, IResourceSystem
        {
            public readonly List<UniTaskCompletionSource<Sprite>> Requests = new();
            public CancellationToken LastToken;
            public GameObject LastOwner;

            async UniTask<T> IResourceSystem.LoadAsync<T>(string address, GameObject bindTo, CancellationToken ct)
            {
                LastToken = ct;
                LastOwner = bindTo;
                var request = new UniTaskCompletionSource<Sprite>();
                Requests.Add(request);
                return (T)(Object)await request.Task;
            }
        }

        private ArchContext _context;
        private DelayedSprites _resources;
        private Image _image;
        private Sprite _sprite;
        private ImageSpriteBinding _binding;

        [SetUp]
        public void SetUp()
        {
            _resources = new DelayedSprites();
            _context = new ArchContext();
            _context.RegisterSystem(_resources);
            _image = new GameObject("Portrait", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _binding = new ImageSpriteBinding(_image);
        }

        [TearDown]
        public void TearDown()
        {
            _binding.Clear();
            Object.DestroyImmediate(_image.gameObject);
            Object.DestroyImmediate(_sprite);
            _context.Shutdown();
        }

        [Test]
        public void SameAddress_ReusesPendingAndDisplayedSprite()
        {
            _binding.SetAddress("A");
            _binding.SetAddress("A");
            Assert.That(_resources.Requests.Count, Is.EqualTo(1));
            Assert.That(_resources.LastOwner, Is.SameAs(_image.gameObject));
            _resources.Requests[0].TrySetResult(_sprite);
            _binding.SetAddress("A");
            Assert.That(_image.sprite, Is.SameAs(_sprite));
            Assert.That(_image.enabled, Is.True);
            Assert.That(_resources.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void RebindAndClear_IgnoreLateResultsEvenWhenTheAddressIsReused()
        {
            _binding.SetAddress("A");
            var first = _resources.LastToken;
            _binding.SetAddress("B");
            Assert.That(first.IsCancellationRequested, Is.True);
            var second = _resources.LastToken;
            _binding.SetAddress(null);
            Assert.That(second.IsCancellationRequested, Is.True);
            _binding.SetAddress("A");
            _resources.Requests[0].TrySetResult(_sprite);
            _resources.Requests[1].TrySetResult(_sprite);
            Assert.That(_image.sprite, Is.Null);
            Assert.That(_image.enabled, Is.False);
            _binding.SetAddress("A");
            Assert.That(_resources.Requests.Count, Is.EqualTo(3));
            _resources.Requests[2].TrySetResult(_sprite);
            Assert.That(_image.sprite, Is.SameAs(_sprite));
            _binding.Clear();
            Assert.That(_image.sprite, Is.Null);
            Assert.That(_image.enabled, Is.False);
        }

        [Test]
        public void FailedLoad_CanRetryTheSameAddress()
        {
            Exception failure = null;
            void OnFailure(Exception exception) => failure = exception;
            UniTaskScheduler.UnobservedTaskException += OnFailure;
            try
            {
                _binding.SetAddress("A");
                _resources.Requests[0].TrySetResult(null);
                Assert.That(failure, Is.TypeOf<ResourceException>());
                _binding.SetAddress("A");
                _resources.Requests[1].TrySetResult(_sprite);
                Assert.That(_image.sprite, Is.SameAs(_sprite));
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnFailure;
            }
        }
    }
}
