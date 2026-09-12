using System;
using July.Release;
using UnityEngine;
using YooAsset;

namespace July.Bootstrap
{
    /// <summary>在共用资源分发设置上补充运行模式；Inspector 中直接显示所有字段。</summary>
    [Serializable]
    public sealed class ResourceStartupConfig : ReleaseResourceSettings
    {
        [Tooltip("编辑器可使用模拟模式；微信和抖音小游戏真机由框架使用 WebPlayMode。")]
        public EPlayMode PlayMode = EPlayMode.EditorSimulateMode;
    }
}
