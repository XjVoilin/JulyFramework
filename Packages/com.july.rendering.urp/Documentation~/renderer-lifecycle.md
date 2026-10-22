# July URP Renderer Lifecycle

显式启用一个闲置 Renderer2D 的资源回收。基线为 Unity 2022.3 / URP 14；通过 RendererData.SetDirty 与 Pipeline.GetRenderer 由 URP 销毁旧实例、更新缓存，新实例的全屏附件在后续绘制时按需建立。无 URP 补丁、直接 Dispose、反射缓存操作或 Blitter 恢复。

## 接入与职责

配置 IdleRendererReclaimConfig 的 Pipeline、明确索引和该索引对应的 Renderer2DData；注册前调用 IdleRendererReclaimSystem.Configure，再沿 July Arch 初始化。UI 保持原有相机组合，无须请求回收；项目宿主只装配配置。

高亮、回收核心与场景接入统一位于 July.Rendering.URP 程序集，显式依赖 URP/Core、July.Arch、July.Scene 和 UniTask。IdleRendererReclaimSystem监听场景加载完成、失败/取消、卸载完成，请求检查；场景事件不直接代表闲置。IdleRendererReclaimer 亦可由框架生命周期独立持有。

## 回收契约

- 只处理明确配置的一个 Renderer2D，当前为默认索引0。不自动回收其他 Renderer 或未知 Feature。
- 记录实际绘制；没有绘制过的不重建。请求期间，若本轮绘制过、任何启用相机或其活动 Overlay 仍使用它，保留请求等待；完全闲置后只重建一次。
- 相机每次经 URP 解析实例，不得跨重建保留旧 Renderer、Pass、Feature 引用。禁用相机的手动绘制在执行期间受保护，之后通过 URP 获取替换实例。
- RendererData 必须对应管线索引；未发生实例替换时快速失败。变更管线列表须同步配置。其他管线活动时暂停回收。
- 重建销毁整个旧实例的材质、Pass、Feature 和附件；启用前须确认兼容。自定义持有资源的使用者不属于自动相机检查范围。
- 关闭模块解除回调/事件订阅，不销毁归 URP 所有的实例。

代价为交接时创建新实例，切回时重建附件。桌面代表路径的结果不替代手机内存、切换耗时、完整 HUD 和新增 Feature 验收。

July.Rendering.URP 运行程序集属于 AOT。发布时沿项目现有构建流程重新生成 HybridCLR 裁剪/泛型引用并重建 Player；仅更新热更 DLL 无法向旧 Player 增加此能力。
