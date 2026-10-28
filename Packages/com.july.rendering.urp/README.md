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

运行时代码统一编译到 `July.Rendering.URP`，包含高亮绘制、多个闲置 Renderer 回收及 July.Arch/Scene 生命周期接入；程序集显式依赖 URP/Core、July.Arch、July.Scene 和 UniTask。功能仍按类和目录组织，高亮组件不创建全局 System，不要求游戏提供绘制回调。包不依赖 July.UI 或游戏身份。

## 同表面深度精度

0.2.1 的可见蒙版采用与普通 URP 材质一致的三维世界坐标投影，并使用 `Offset 0, -1` 提供一个深度单位的同表面容差；不使用随斜率增长的偏移，不写场景深度。小于该数值精度的间隔不会被可靠区分为遮挡。测试覆盖同表面零/一个单位差值及明确的64单位深度间隔，不能代替所有手机/接触边界的设备验收。

## 从 0.1.0 升级

组件、Shader GUID、序列化字段、材质参数名称及选中接口保持一致，无需迁移现有 Prefab。复合物品按合并轮廓着色一次，边缘由屏幕蒙版生成，与旧版逐子网格染色及法线扩张并非逐像素一致。需要按上方条件检查相机深度附件和 MSAA 配置。

渲染代码属于 AOT；升级后须重建 Player 及对应资源，单独更新热更程序集不能替换旧渲染实现。

## 验证与限制

`Tests/Editor` 提供组件生命周期检查，集成工程可在 manifest 的 `testables` 中加入本包后运行。

0.2.0 的隔离集成检查覆盖材质保持、遮挡、取消、屏幕边缘及 GLES3 六程序编译；额外的实际 Shader 连续帧夹具验证了外辉透明度过渡。此前 PC GPU/CPU、RT 生命周期测试提供桌面证据，外辉平滑后未重新采集完整性能矩阵。微信 iOS/Android 的效果、内存与持续性能仍待设备验收；发布标签不代表已完成设备验收或已证明所有场景更快。

0.2.0 的 iPhone 双色诊断确认过无遮挡物品出现误遮挡；0.2.1 针对此机制修正，实际 iPhone 复测仍待完成。

## 闲置 Renderer 回收

`RendererLifecycleConfig` 配置 Pipeline 和 Renderers 列表；每项填写明确的 RendererIndex 及对应 ScriptableRendererData。注册 RendererLifecycleSystem 后，场景加载完成、失败/取消和卸载完成请求检查；每个目标独立记录绘制和等待状态，在帧末确认无相机或活动相机栈使用时，经公开 SetDirty/GetRenderer 释放旧实例并替换缓存。支持为 2D 与 3D 场景交接配置多个 Renderer，UI 无须管理这些资源。

原单项 RendererIndex/RendererData 配置需迁移到 Renderers 列表。只回收显式列出的目标；项目应验证目标及 Feature 的重建行为，调用方不得跨重建保留旧实例或 Pass 引用。关闭模块仅解除订阅，不释放管线实例。类型、命名空间、脚本 GUID 和单一 July.Rendering.URP 运行程序集保持。

配置、有效期和发布要求见[生命周期说明](Documentation~/renderer-lifecycle.md)。正式发布需重建 AOT Player 及 HybridCLR 配置。桌面检查不替代手机内存与切换耗时验收。

## Editor 场景出图

入口：**JulyGF → 渲染 → 场景出图**。选择当前场景的正交相机和目标BG的MeshRenderer，点击“导出PNG”。

- BG应为平面网格，UV为连续矩形的仿射映射。工具读取实际网格与UV，自动确定完整取景、图片方向和比例；长边固定2048，短边自动计算。没有手动像素宽度或比例输入。
- 默认路径Assets/场景名.png，可编辑或选择；存在则直接覆盖。项目内同时生成同目录的`场景名_Baked.mat`，处理原BG图集UV范围使用July/URP/Baked Surface，保持贴图原色并接收主光实时阴影，避免重复受光。
- 回贴时将生成的`_Baked.mat`赋给原BG，并隐藏已经合成进图片的3D物件。不能只把PNG换进原来的受光材质；保存到项目外只输出PNG。
- 使用当前已打开场景及未保存修改，不重新加载或保存源场景，不按物件名称/层级筛选，原有相机可见性仍生效。
- 捕获使用临时相机与BG的UV投影，复制URP附加数据并渲染Base/Overlay相机栈；不复制业务相机脚本。完成或异常时释放临时资源。
- 项目内PNG同步导入后绑定Console日志context；点击新导出日志可定位PNG。

功能位于独立的July.Rendering.URP.Editor程序集，不进入Player。纹理只保存当前相机方向下的静态外观，不保存场景深度或后续动态阴影。

### 烘焙贴图的实时阴影

`July/URP/Baked Surface`直接显示烘焙贴图颜色，只叠加URP主光阴影衰减，不重新计算光照颜色、强度、高光或环境光。出图工具默认使用此Shader；已有材质保留原_BaseMap与UV缩放/偏移即可切换。
材质的`Received Shadow Strength`默认0.5，0保持原图，1使用完整阴影衰减。支持主光硬阴影、软阴影、级联及屏幕空间阴影关键字，提供DepthOnly Pass，不额外投射地板阴影。
纹理中已有的静态阴影会保留；若对应3D投影物件继续存在，应避免重复烘入同一份阴影。阴影接收使用真实BG平面深度，不能恢复图中花盆或其他装饰原本的立体遮挡。

## Editor 节点出图

独立入口：**JulyGF → 渲染 → 节点出图**。场景/BG出图仍用于按原BG边界和UV回贴，不合并入口。

1. 在Hierarchy选中要替换的节点，指定取景相机及Assets内的PNG路径。
2. 默认开启“保留地板实际阴影”，将原地板Renderer拖入“接影地板”。地板需处于节点外部、当前可见、水平且不透明。无需更换地板材质，无需调阴影强度。
3. 点击“导出并生成面片”。沿源相机视线将节点与主方向光投影范围映射到地板XZ平面，按世界宽长和原相机像素密度计算图片大小，不固定2048。旋转及透视烘入图片。
4. 原节点同级创建或更新`节点名_Image`，配置透明URP Unlit材质及持久网格，补偿父级旋转、非均匀/镜像缩放。隐藏原节点后对比。原地板保持原样。

阴影采用三次渲染：透明节点、原地板接收该节点投影、原地板不接收该节点投影。根据后两者实际颜色差异，在运行时混合所用的颜色空间中求解RGBA覆盖层，再合成节点图像。地板材质、纹理、环境光和光源的影响由渲染结果决定；不再使用模拟黑色接影Shader或手动强度。Unlit等本来不接影的地板不会被添加影子。PNG不包含原地板底色，需叠在该原地板上使用。

开启阴影时，捕获平面取原地板高度，生成面片高出0.001世界单位，避免共面深度遮挡。关闭阴影时无需地板，平面高度取原节点世界Y。面片世界旋转为0、几何在XZ平面，尺寸存在网格中，localScale为1。面片无Collider，不额外受光或投影；固定视角图片不能恢复3D深度。地板材质或照明改变后需重新导出。

PNG旁保存`文件名_Image.mat`与`文件名_Quad.asset`，同一路径重复导出更新同一父级的对应面片，支持Undo/Redo；场景仍需手动保存。PNG与面片日志分别定位对应资源。节点与其他Renderer的可见性及投影模式在成功或异常后恢复，原相机及业务/碰撞组件不修改。

相机需向下观察，透视相机需高于节点及目标平面，节点包围盒需位于相机前方。阴影范围扩展按主方向光估计；后处理/HDR与Overlay相机栈不参与本入口。不能从原地板已有贴图或光照贴图中分离出不可切换的预烘焙阴影。本入口面向场景Renderer，非Prefab资产、Terrain或Screen Space Overlay UI截图工具。

选中父节点可包含其全部可见子Renderer；一个根节点只包含自己的子树，并不等同于场景内所有根节点。若目标是回贴现有BG，继续使用场景/BG出图，保留其原有边界、UV和配套材质处理。

场景出图与节点出图分别按项目记住上次保存路径，关闭窗口、重编译或重启Editor后恢复；节点窗口重新打开或切换当前选择不再清空路径。
