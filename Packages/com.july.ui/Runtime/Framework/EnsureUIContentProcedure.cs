using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 确保专用 Host 下存在一个铺满区域的 UI Prefab 实例。
    /// Host 已有子节点时视为内容已准备完成。
    /// </summary>
    public sealed class EnsureUIContentProcedure : ProcedureBase
    {
        private readonly string _assetName;
        private readonly RectTransform _host;

        public EnsureUIContentProcedure(string assetName, RectTransform host)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                throw new ArgumentException("UI 资源名称不能为空。", nameof(assetName));

            _assetName = assetName;
            _host = host != null
                ? host
                : throw new ArgumentNullException(nameof(host));
        }

        protected override async UniTask OnExecuteAsync(CancellationToken ct)
        {
            if (_host.childCount > 0)
                return;

            var instance = await this.GetSystem<IResourceSystem>()
                .InstantiateAsync(_assetName, _host, ct);

            if (instance == null)
                throw new InvalidOperationException($"UI 资源 {_assetName} 实例化失败。");

            StretchToHost(instance.transform as RectTransform);
        }

        private static void StretchToHost(RectTransform content)
        {
            if (content == null)
                return;

            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
        }
    }
}
