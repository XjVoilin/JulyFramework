using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    internal sealed class AssetCheckThumbnail : Image
    {
        private readonly IVisualElementScheduledItem _previewPoll;
        private Object _asset;

        internal static bool Supports(AssetCheckKind kind) => kind == AssetCheckKind.Texture ||
            kind == AssetCheckKind.Model || kind == AssetCheckKind.Material;

        internal AssetCheckThumbnail()
        {
            name = "thumbnail";
            AddToClassList("resource-thumbnail");
            scaleMode = ScaleMode.ScaleToFit;
            _previewPoll = schedule.Execute(UpdatePreview).Every(100);
            ResetPreview();
        }

        internal void Bind(string id)
        {
            ResetPreview();
            if (id == null) return;
            // 快照中的资源可能已被移动或删除，始终通过 GUID 解析当前位置。
            var path = AssetCheckTarget.Resolve(id);
            if (string.IsNullOrEmpty(path)) return;
            _asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (_asset == null) return;
            style.display = DisplayStyle.Flex;
            tooltip = path;
            if (_asset is Texture texture)
                image = texture;
            else
            {
                // 模型、材质和图集沿用 Unity 的异步预览；不可用时显示原生资源图标，不修改资源。
                image = AssetPreview.GetMiniThumbnail(_asset);
                UpdatePreview();
            }
        }

        internal void ResetPreview()
        {
            _previewPoll.Pause();
            _asset = null;
            image = null;
            tooltip = "";
            style.display = DisplayStyle.None;
        }

        private void UpdatePreview()
        {
            // 异步生成预览期间，用户可能删除资源或切换列表绑定。
            if (_asset == null) { ResetPreview(); return; }
            var preview = AssetPreview.GetAssetPreview(_asset);
            if (preview != null)
            {
                image = preview;
                _previewPoll.Pause();
            }
            else if (AssetPreview.IsLoadingAssetPreview(_asset.GetInstanceID()))
                _previewPoll.Resume();
            else
                _previewPoll.Pause();
        }
    }
}
