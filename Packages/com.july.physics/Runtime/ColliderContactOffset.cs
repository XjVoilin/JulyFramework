using System;
using UnityEngine;

namespace July.Physics
{
    /// <summary>随 Prefab 保存并应用当前物体上三维碰撞体的接触偏移。</summary>
    [DisallowMultipleComponent]
    public sealed class ColliderContactOffset : MonoBehaviour
    {
        [SerializeField, InspectorName("接触偏移")] [Min(0.001f)]
        private float _contactOffset = 0.01f;

        private Collider[] _colliders;

        private void Awake()
        {
            _colliders = GetComponents<Collider>();
            if (_colliders.Length == 0)
                throw new InvalidOperationException(
                    $"{name}: ColliderContactOffset requires a Collider on the same GameObject.");
        }

        private void OnEnable()
        {
            foreach (var c in _colliders)
                if (c.enabled)
                    c.contactOffset = _contactOffset;
        }

        /// <summary>在 Awake 后设置本物体的碰撞体启停；启用形状后恢复 Prefab 配置。</summary>
        public void SetEnabled(bool e)
        {
            foreach (var c in _colliders)
            {
                c.enabled = e;
                // 非激活物体还没有原生形状；激活时由 OnEnable 应用。
                if (e && gameObject.activeInHierarchy) c.contactOffset = _contactOffset;
            }
        }
    }
}