# Changelog

## Unreleased

- 透明节点图片新增Received Shadow Strength（0～1，默认1），可减弱接影以匹配原模型观感，重复导出保留该值。

- 节点出图使用透明Baked Transparent Surface材质并开启Renderer接影；保持烘焙原色/Alpha，仅叠加实时主光阴影，不重复受光。同路径重导出更新旧面片，保留覆盖与Undo/Redo；需Universal Renderer开启Transparent Receive Shadows。

- 新增独立自阴影烘焙EditorWindow：选择接收网格/自投影节点/方向光/相机，克隆URP捕获UV阴影到原底图Alpha，生成并应用专用材质，保留动态接影；含禁用替身重烘、UV冲突检查、路径记忆、覆盖和场景Undo。Shader通过_BakedSelfShadow/_BAKED_SELF_SHADOW约定接入，无Game110依赖。

- Baked Surface新增默认开启的Receive Shadows材质开关；关闭时通过本地Shader关键字跳过阴影坐标计算、采样与衰减叠加，保留已有材质效果。

- 修复节点出图重新打开或切换选择清空保存路径；两个窗口分别按项目持久记住上次路径。

- 节点出图改为指定原接影地板，通过有/无节点投影的实际颜色差异提取透明阴影；遵循原材质、纹理及环境光效果，移除模拟接影Shader及手动强度滑条。面片叠在原地板上使用，并抬高0.001避免共面遮挡。
- 节点出图沿源相机视线映射到XZ平面，旋转与透视烘入图片，宽长与分辨率按节点及阴影范围自动计算；透视捕获保留源相机远裁面以保持阴影淡出距离。
- 新增独立节点出图窗口，在原节点同级生成透明面片，保存材质与网格，自动配置变换并补偿父级缩放；同路径重复导出更新结果，支持Undo/Redo，原节点保留。场景/BG出图入口保持独立。

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
