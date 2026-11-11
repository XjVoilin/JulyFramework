using System;
using UnityEngine;

namespace July.Physics.Authoring
{
    /// <summary>解析单组制作目标，并为生成请求接入项目适配。</summary>
    public sealed class ColliderAuthoringContext
    {
        public ColliderGenerationRequest Request;
        internal static ColliderGenerationRequest BuildRequest(IColliderAuthoringAdapter adapter, GameObject root, ColliderAuthoringProfile profile, string targetId)
        {
            var request = adapter.CreateRequest(root, profile, targetId);
            request.AfterApply = result => adapter.Complete(root, new[] { new ColliderTargetResult(targetId, result) });
            return request;
        }
        public static ColliderAuthoringContext Create(Transform outputRoot, ColliderGenerationSettings settings)
        {
            var root = outputRoot.root.gameObject;
            var adapter = ColliderAuthoringAdapters.Resolve(root); var profile = adapter.CreateProfile(); ColliderAuthoringSettings.Load(profile);
            var layout = adapter.Inspect(root, profile);
            if (layout.Error != null) throw new InvalidOperationException(layout.Error);
            int index = Array.FindIndex(layout.Targets, t => t.OutputRoot == outputRoot);
            if (index < 0) throw new InvalidOperationException("所选节点不是当前配置的输出分组，请在制作设置中指定路径。");
            var target = profile.Targets[index];
            if (target.FixedGoal && settings.Goal != target.Settings.Goal)
                throw new InvalidOperationException("项目已固定该输出的生成方式，请使用对应模式。");
            var request = BuildRequest(adapter, root, profile, target.Id); request.Settings = settings;
            return new ColliderAuthoringContext { Request = request };
        }
    }
}
