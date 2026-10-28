# Changelog

## Unreleased

- 新增独立节点出图窗口：保留源相机投影，按节点屏幕像素范围裁切PNG，并在原节点同级生成透明面片，自动设定材质/网格/变换并补偿父级缩放；同路径重复导出更新结果，支持Undo/Redo，原节点保留。



- 新增Baked Surface Shader，保持烘焙贴图原色，仅接收主光实时阴影；场景出图配套材质改用此Shader，避免Unlit无阴影或Lit重复受光。

- 新增场景出图EditorWindow：从当前场景的正交相机及平面BG的实际UV自动计算完整取景和比例，长边2048；导出PNG及配套Unlit回贴材质，支持可变路径、覆盖和Console资源定位，不修改源场景、不筛选物件。



- 将对外类型统一命名为 RendererLifecycleSystem / RendererLifecycleConfig，内部 IdleRendererReclaimer 保持；调用方需同步类型名称，回收行为和脚本 GUID 不变。

- 闲置回收配置改为多个 Renderer 条目，Data 使用 ScriptableRendererData；各项独立记录绘制和回收请求，支持 2D/3D 场景双向释放。旧单项配置需迁移到 Renderers 列表。

- 将闲置 Renderer2D 回收并入原包，运行时代码统一归属 July.Rendering.URP；删除额外生命周期程序集，保持源码命名空间和 Unity GUID。
- 明确增加 July.Arch、July.Scene、UniTask 程序集引用及对应包依赖，不依赖 July.UI 或游戏身份。
- 保持公开 SetDirty/GetRenderer 回收与相机使用者保护；不修改 URP 源码，不直接 Dispose Renderer，不恢复 Blitter。正常发布时重新生成 HybridCLR 引用/裁剪配置并重建 AOT Player。

## 0.2.1 - 2026-10-20

- Match the silhouette vertex projection to ordinary URP surfaces (world xyz, then constant-w projection).
- Bias only the visible mask by one depth unit, without slope bias, to tolerate same-surface rounding differences between independently compiled passes. Do not write scene depth or change the complete silhouette/composite.
- Add a rendered depth regression: a one-unit difference must remain visible, while a 64-unit depth separation must still be occluded. iPhone red/green diagnostics confirmed self-occlusion in 0.2.0; this patch still requires device retesting.


## 0.2.0 - 2026-10-20

- Replace shared-stencil directional geometry with complete/visible RG8 masks and one local composite; preserve component/Shader GUIDs and material parameter names.
- Project individual renderer bounds and discard off-screen regions before union to reduce oversized composite rectangles.
- Smooth animated outer glow using linear coverage sampling while retaining point-sampled silhouette, outline and occlusion rules.
- Require orthographic Base cameras, MSAA=1, a renderable RG8 attachment and addressable scene depth; reuse original static meshes without material/mesh clones or scene-color copies.
- Release mask resources on feature recreation/disposal/disable. Keep selection cancellation and camera-local lifecycle contracts.
- Validated PC behavior, animated edge coverage and GLES3 compilation; WeChat iOS/Android device acceptance remains pending. Rebuild the Player/AOT code and resources when upgrading.


## 0.1.0 - 2026-10-19

- Initial reusable orthographic URP static-mesh highlighting, extracted from the validated Game110 effect.
- Camera-local candidate, compound static-mesh target, shared stencil stages, instanced direction draws, and configurable shared-material shader.
