using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal delegate void AssetCheckForm(VisualElement parent, PlatformStandards standards, Action<Action<PlatformStandards>> edit);

    internal sealed class AssetCheckRegistration
    {
        internal readonly Func<IAssetCheckModule> CreateModule;
        internal readonly string Name;
        internal readonly string Notice;
        internal readonly bool ProjectWide;
        private readonly Func<PlatformStandards, object> _standards;
        internal readonly AssetCheckForm Form;
        internal readonly AssetCheckKind Kind;

        internal AssetCheckRegistration(AssetCheckKind kind, Func<IAssetCheckModule> createModule, string name, string notice,
            Func<PlatformStandards, object> standards, AssetCheckForm form, bool projectWide = false)
        {
            Kind = kind;
            CreateModule = createModule;
            Name = name;
            Notice = notice;
            _standards = standards;
            ProjectWide = projectWide;
            Form = form;
        }

        internal string Context(AssetCheckSettings settings) => settings.platform + "|" +
            (ProjectWide ? "项目配置" : settings.scope) + "|" +
            (_standards(settings.CurrentStandards) is object value ? JsonUtility.ToJson(value) : string.Empty);
    }

    internal static class AssetCheckCatalog
    {
        // 全量扫描与类型页签共用这一份显式注册的模块列表。
        internal static readonly AssetCheckRegistration[] Entries =
        {
            new AssetCheckRegistration(AssetCheckKind.Texture, () => new TextureCheckModule(), "贴图", "格式和尺寸修改仅处理目标平台；Read/Write 影响所有平台。图集尺寸检查配置上限。", s => s.texture, TextureStandardsView.Build),
            new AssetCheckRegistration(AssetCheckKind.Model, () => new ModelCheckModule(), "模型", "模型导入修改对整个文件、所有平台生效；独立 Mesh 仅检查面数。", s => s.model, ModelStandardsView.Build),
            new AssetCheckRegistration(AssetCheckKind.Material, () => new MaterialCheckModule(), "材质", "检查 Shader 和失效引用；正常未赋值不算问题。", s => null, AssetCheckStandardFields.Integrity),
            new AssetCheckRegistration(AssetCheckKind.PrefabScene, () => new PrefabSceneCheckModule(), "预制体／场景", "检查磁盘中已保存的资源；当前有未保存修改的场景标记未能验证。", s => null, AssetCheckStandardFields.Integrity),
            new AssetCheckRegistration(AssetCheckKind.Audio, () => new AudioCheckModule(), "音频", "检查当前编辑器导入结果的采样率和声道；不模拟小游戏 SDK，也不自动修改音频。", s => s.audio, AudioStandardsView.Build),
            new AssetCheckRegistration(AssetCheckKind.Urp, () => new UrpCheckModule(), "URP", "按项目 Graphics／目标平台 Quality 收集管线和 Renderer；不受资源目录限制。修改影响全部使用者。", s => s.urp, UrpStandardsView.Build, true),
            new AssetCheckRegistration(AssetCheckKind.Project, () => new ProjectCheckModule(), "项目设置", "检查整个项目的目标平台配置，不受资源目录限制；修改沿用 Unity 设置或发布工具。", s => s.project, ProjectStandardsView.Build, true)
        };
    }
}
