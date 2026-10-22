# July Rendering URP

URP 正交相机的静态网格高亮：表面着色、黑色描边、黄色外辉，以及遮挡部分的扫描填充。

## 接入

1. 在相机使用的 Universal Renderer Data 中添加 `MeshHighlightRendererFeature`。包程序集属于普通主包/AOT，不依赖游戏热更程序集。
2. 在正交 Base 相机上添加 `MeshHighlightCamera`。每个相机保存一个候选目标；没有有效选中目标时不提交高亮 Pass。
3. 在物品显示节点上添加 `MeshHighlightTarget`，配置静态 MeshRenderer 列表和使用 `July/URP/Mesh Highlight` Shader 的共享高亮材质。初始化后不替换列表或网格。
4. 选中和取消时由业务方更新相机候选及目标状态：

```csharp
cameraHighlight.Target = target;
target.SetHighlighted(true);
// On deselection:
target.SetHighlighted(false);
cameraHighlight.Target = null;
```

目标隐藏或组件禁用时清除高亮标记；再次启用不自动恢复选中。相机接点禁用时清除候选。调用方继续拥有正常退出和销毁顺序。

## 配置与成本

- 已验证 Unity 2022.3 / URP 14，正交相机、MSAA=1、不透明静态 MeshRenderer、Base+Overlay 相机栈。需要可绑定的场景深度附件；直接渲染到不可寻址 CameraTarget 深度的配置会报错。
- 一个目标支持多个节点、实时变换和多个 SubMesh。复用原 sharedMesh，不克隆 Mesh 或 Material，不修改普通表面材质或 Renderer MaterialPropertyBlock；不需要高亮材质开启 GPU Instancing。
- 在 AfterRenderingOpaques 写入 RG8 蒙版：R 为完整轮廓，G 为通过场景深度测试的可见区域；随后在目标屏幕矩形内合成一次。每个有效子网格两次几何提交，另一次合成。不占模板位、不写场景深度，不为本效果启用场景颜色复制或全屏模糊。
- 逐 Renderer 投影并排除完全出屏节点，减少复合物品的无效合成区域。蒙版本身仍为相机全尺寸，1080×1920 的 RG8 颜色存储约 3.96 MiB；取消时不绘制，纹理保留供后续选中复用，重建和 Dispose 时释放。
- 颜色、宽度、强度及遮挡扫描参数由共享材质配置。内部、遮挡和黑边保持点采样规则；黄色外辉使用独立线性过滤，保留亚像素覆盖透明度以平滑呼吸变化。
- 限定 RG8 可渲染格式与 GLES3 级 Shader 支持；透视相机、MSAA、SkinnedMeshRenderer、单相机多个并发选中目标、其他渲染管线及 RenderGraph 不在当前范围。

运行时代码统一编译到 `July.Rendering.URP`，包含高亮绘制、闲置 Renderer2D 回收及 July.Arch/Scene 生命周期接入；程序集显式依赖 URP/Core、July.Arch、July.Scene 和 UniTask。功能仍按类和目录组织，高亮组件不创建全局 System，不要求游戏提供绘制回调。包不依赖 July.UI 或游戏身份。

## 同表面深度精度

0.2.1 的可见蒙版采用与普通 URP 材质一致的三维世界坐标投影，并使用 `Offset 0, -1` 提供一个深度单位的同表面容差；不使用随斜率增长的偏移，不写场景深度。小于该数值精度的间隔不会被可靠区分为遮挡。测试覆盖同表面零/一个单位差值及明确的64单位深度间隔，不能代替所有手机/接触边界的设备验收。

## 从 0.1.0 升级

组件、Shader GUID、序列化字段、材质参数名称及选中接口保持一致，无需迁移现有 Prefab。复合物品按合并轮廓着色一次，边缘由屏幕蒙版生成，与旧版逐子网格染色及法线扩张并非逐像素一致。需要按上方条件检查相机深度附件和 MSAA 配置。

渲染代码属于 AOT；升级后须重建 Player 及对应资源，单独更新热更程序集不能替换旧渲染实现。

## 验证与限制

`Tests/Editor` 提供组件生命周期检查，集成工程可在 manifest 的 `testables` 中加入本包后运行。

0.2.0 的隔离集成检查覆盖材质保持、遮挡、取消、屏幕边缘及 GLES3 六程序编译；额外的实际 Shader 连续帧夹具验证了外辉透明度过渡。此前 PC GPU/CPU、RT 生命周期测试提供桌面证据，外辉平滑后未重新采集完整性能矩阵。微信 iOS/Android 的效果、内存与持续性能仍待设备验收；发布标签不代表已完成设备验收或已证明所有场景更快。

0.2.0 的 iPhone 双色诊断确认过无遮挡物品出现误遮挡；0.2.1 针对此机制修正，实际 iPhone 复测仍待完成。

## 闲置 Renderer2D 回收

通过 `IdleRendererReclaimSystem.Configure(IdleRendererReclaimConfig)` 配置明确的 Pipeline、Renderer 索引及对应 Renderer2DData，再沿 July Arch 注册和初始化。场景加载完成、失败/取消和卸载完成只请求检查；核心在帧末确认本轮未绘制、启用相机和活动堆栈均不使用后，通过公开 SetDirty/GetRenderer 释放旧实例并替换缓存。UI 继续负责自己的相机组合，无须管理 Renderer 资源。

生命周期配置/类型保留原命名空间和Unity GUID，统一归属 `July.Rendering.URP` 程序集；调用方只引用该程序集，没有独立 lifecycle 包或运行程序集。回收会重建旧实例的材质、Pass 和 Feature，只对显式验证过的 Renderer2D 启用；当前不自动处理 UniversalRenderer 或未知 Feature。关闭模块只解除自身订阅。

配置和占用契约见[生命周期说明](Documentation~/renderer-lifecycle.md)。运行程序集属于 AOT，正常发布需重新生成 HybridCLR 引用/裁剪配置并重建 Player，不能仅更新热更 DLL。桌面验证不能替代完整 HUD 与手机内存、切换耗时验收。
