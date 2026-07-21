using System;
using UnityEngine;

namespace July.Resource
{
    /// <summary>
    /// 资源句柄 — 包装底层资源的引用计数。
    /// 释放完全委托给底层（如 YooAsset AssetHandle.Release）。
    /// 支持 Dispose / BindTo / scope 三种释放方式。
    /// </summary>
    public sealed class ResourceHandle<T> : IDisposable where T : UnityEngine.Object
    {
        public T Asset { get; private set; }
        public bool IsValid => Asset != null && _release != null;
        public bool IsPermanent { get; private set; }

        private Action _release;
        private ResourceHandleTracker _tracker;

        public ResourceHandle(T asset, Action release)
        {
            Asset = asset;
            _release = release;
        }

        public ResourceHandle<T> MarkPermanent()
        {
            IsPermanent = true;
            return this;
        }

        public void BindTo(GameObject gameObject)
        {
            if (_release == null || gameObject == null) return;

            if (_tracker != null)
            {
                UnityEngine.Object.Destroy(_tracker);
                _tracker = null;
            }

            _tracker = gameObject.AddComponent<ResourceHandleTracker>();
            _tracker.Initialize(this);
        }

        public void BindTo(Component component)
        {
            if (component != null)
                BindTo(component.gameObject);
        }

        public void Dispose()
        {
            if (_release == null) return;

            var release = _release;
            _release = null;
            Asset = null;

            if (_tracker != null && _tracker.gameObject != null)
                UnityEngine.Object.Destroy(_tracker);
            _tracker = null;

            release();
        }
    }

    internal class ResourceHandleTracker : MonoBehaviour
    {
        private IDisposable _handle;
        private static bool _isApplicationQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _isApplicationQuitting = false;
        }

        internal void Initialize(IDisposable handle)
        {
            _handle = handle;
        }

        private void OnApplicationQuit()
        {
            _isApplicationQuitting = true;
        }

        private void OnDestroy()
        {
            if (!_isApplicationQuitting)
                _handle?.Dispose();
            _handle = null;
        }
    }
}
