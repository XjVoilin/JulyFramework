# July URP Renderer Lifecycle

显式配置多个 Renderer 的闲置回收。基线为 Unity 2022.3 / URP 14；通过 RendererData.SetDirty 与 Pipeline.GetRenderer 由 URP 销毁旧实例、更新缓存，新实例的全屏附件在后续绘制时按需建立。不修改 URP，不直接 Dispose Renderer，不操作反射缓存或恢复 Blitter。

## 接入与配置

`RendererLifecycleConfig.Pipeline` 指定管线；`Renderers` 的每一项填写该管线 Renderer List 中的 `RendererIndex` 和对应的 `ScriptableRendererData`。支持配置 Renderer2DData 和 UniversalRendererData；项目负责确认所配 Renderer 及其 Feature 支持释放后重新创建。索引必须非负，列表非空，不允许重复索引或重复 Data；调整管线列表时同步配置。

例如管线索引 0 使用 Renderer2DData、索引 1 使用 UniversalRendererData 时，将两项均加入列表。进入 3D 场景后回收闲置的 2D Renderer；返回 2D 场景后回收闲置的 3D Renderer。未列出的 Renderer 不参与回收。旧的单项 RendererIndex/RendererData 配置须迁移至 Renderers 列表。

注册前调用 RendererLifecycleSystem.Configure，再沿 July Arch 初始化。UI 保持原有相机组合，无须请求回收；项目宿主只装配配置。高亮、回收核心与场景接入统一位于 July.Rendering.URP 程序集，依赖 URP/Core、July.Arch、July.Scene 和 UniTask。

## 调度与有效期

- 场景加载完成、失败/取消及卸载完成只请求检查，不直接证明闲置。
- 每项分别记录请求和实际绘制。某项正在使用不会阻塞其他项回收；没有绘制过的新实例不重复重建。
- 帧末若本轮没有绘制、所有启用相机及其活动 Overlay 均不使用该实例，才执行重建；仍被使用的项保留请求等待。
- 其他管线活动时暂停回收。RendererData 必须与管线索引对应，未发生实例替换时抛出带索引的异常。
- 相机每次经 URP 解析实例，不得跨回收保存旧 Renderer、Pass 或 Feature 内部资源引用。禁用相机的手动绘制在执行期间受到保护，后续绘制通过 URP 获取新实例。
- 回收会执行整个旧实例及其 Feature 的释放流程，包括颜色、深度、阴影、后处理和自定义 Feature 所持资源；实际释放范围由该 Renderer 实现决定。它并不卸载 RendererData 资源。
- 关闭模块解除事件和渲染回调，取消所有待处理请求，不销毁归 URP 所有的实例。

代价为交接时创建新实例，切回时重建附件。新增 Renderer 或 Feature 时验证其重建行为；桌面验证不替代手机内存、切换耗时及画面验收。

July.Rendering.URP 属于 AOT。正式发布沿项目正常构建流程生成 HybridCLR 裁剪/泛型引用并重建 Player；仅更新热更 DLL 无法替换此能力。
