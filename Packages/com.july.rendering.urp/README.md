# July Rendering URP

URP 正交相机的静态网格高亮：表面着色、黑色描边、外发光，以及遮挡部分的扫描填充。

## 接入

1. 在相机使用的 Universal Renderer Data 中添加 `MeshHighlightRendererFeature`。包程序集是普通主包程序集，可安全地被启动管线引用；不依赖热更程序集。
2. 在正交场景相机上添加 `MeshHighlightCamera`。每个相机独立保存一个当前候选对象；没有该组件或目标时不提交高亮 Pass。
3. 在物品显示节点上添加 `MeshHighlightTarget`，通过 Inspector 配置完整的 MeshRenderer 列表和共享高亮材质。材质使用 `July/URP/Mesh Highlight` Shader，必须开启 GPU Instancing。初始化后不更换配置或网格。
4. 选中时把目标赋给 `cameraHighlight.Target`，并调用 `target.SetHighlighted(true)`；取消时清除候选并调用 `SetHighlighted(false)`。调用方继续拥有选中规则和业务生命周期。

```csharp
cameraHighlight.Target = target;
target.SetHighlighted(true);
// On deselection:
target.SetHighlighted(false);
cameraHighlight.Target = null;
```

目标隐藏或组件禁用时停止绘制并清除高亮标记；再次启用不自动恢复选中。相机接点禁用时清除候选。销毁的目标遵循 Unity 原生空值语义；正常退出仍应先解除选中，再销毁场景对象。

## 配置和有效范围

- Unity 2022.3，URP 14；正交相机，静态 MeshRenderer，单相机单个复合目标。一个目标可有多个显示节点、不同实时变换与多个 SubMesh。
- 正常显示材质与高亮材质独立。颜色、宽度、强度和遮挡噪声在共享材质配置；包不修改表面 Renderer 的材质或 MaterialPropertyBlock。
- 使用原 sharedMesh 及原法线，不克隆 Mesh 或 Material。每目标缓存绘制数据、四份矩阵与方向，复用 MaterialPropertyBlock；每可见显示节点/子网格为 3 次四实例方向绘制 + 5 次普通绘制，共 8 次提交。
- 渲染阶段为 AfterRenderingTransparents，借用当前相机的颜色/深度/模板缓冲，不创建全屏 RenderTexture。模板位 1（数值 2）由本效果使用；接入项目须避开该模板位冲突，并提供深度/模板缓冲。
- 支持隐藏子节点和禁用 Renderer：对应表面不参与任何高亮阶段。边界验证只发生在初始化；配置错误快速失败，没有运行时降级绘制或关闭实例化的备用路径。
- 透视相机、SkinnedMeshRenderer、同时高亮多个候选、其他渲染管线及 RenderGraph 不属于首版范围。

运行代码依赖 URP/Core，不依赖 July.Arch、July.UI、游戏系统或物品身份。不创建全局 System，也不要求小游戏实现 CommandBuffer 回调。

## 验证

`Tests/Editor` 包含原生组件生命周期检查；在集成 Unity 工程的 manifest 中将本包加入 `testables` 后由 Test Runner 运行。完整效果、绘制次数和构建变体在集成工程验证，实际微信/GLES 设备仍需项目构建验收。修改包 C# 后需要重建主包；效果材质参数可随项目资源更新。
