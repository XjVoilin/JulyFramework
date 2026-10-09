# July.UI URP camera composition

UI 0.2.28 requires July.Scene 0.1.1 and URP 14.0.12 or a compatible newer version.

UISystem owns the persistent UI camera and its composition lifetime. No project-level
CameraStackHandler, CameraStackBinder or per-scene preparation call is required.
Use ISceneSystem for runtime scene loading and unloading. UI subscribes to events only;
it does not require SceneSystem to initialize before UISystem.

## Renderer selection

- The UI camera uses the pipeline default renderer while standalone. When binding a
  scene camera, composition selects the same renderer instance before adding UI as
  an Overlay. Detaching restores the default selection (`SetRenderer(-1)`).
- The default renderer is supported without configuration. For other scene renderers,
  set `UIConfig.AdditionalCameraRendererIndices` to their indices in the active URP
  Asset's Renderer List (for example `[1]` when a new renderer was appended at index 1).
  Configure this before UISystem initialization. The list is copied on initialization.
- Matching uses public `GetRenderer(index)` and `scriptableRenderer` APIs, not private
  field reflection. A non-default scene renderer absent from this list fails binding
  explicitly; existing project overlays are preserved and UI remains standalone.
- Select the scene camera renderer before the scene-load-complete binding event.
  Changing it later without a scene lifecycle event is not automatically tracked.
- This changes camera composition only. It does not enable lights, shadows or
  post-processing. Renderer Features shared by the scene and UI cameras must be
  scoped appropriately, e.g. game outlines should not run on the UI Overlay.

## Scene contract

- Exactly one enabled MainCamera may output the scene. It must be a URP Base camera,
  use a stacking-capable renderer, and target the UI display without a RenderTexture.
- With no enabled MainCamera (launch/UI-only scenes), UI renders independently as Base
  over black. This is an explicit supported mode; a gameplay scene must provide its camera.
- Existing project overlays remain ordered. Framework UI is appended last.
- Projects own scene-camera culling, projection, post-processing and positioning.
- On a Single load, UI becomes Base before loading. Completion, failure or cancellation
  re-evaluates the available camera. Unloading the bound scene detaches UI before unload.
- Additive loads must not introduce a second enabled MainCamera. Dynamic camera replacement
  outside scene lifecycle events and split-screen/multi-display UI are not supported here.

Destroying UISystem detaches only its own UI entry. Do not also run a project binder.
Shutdown also tolerates Unity destroying the persistent UI camera before the system lifecycle completes.


## 统一输入接入（2026-09-28）

- 在 UISystem 前注册并初始化 July Input。UI 只依赖 IInputGate，不另存全局阻断计数。
- UISystem 接管单个 StandaloneInputModule，将运行期处理交给内部 JulyStandaloneInputModule；保留原 EventSystem 和导航配置。未接管前启动重试 UI 继续使用 Unity 输入。
- 只支持当前 legacy UGUI 后端；其他输入模块或并存多个模块会明确报错，不静默替换未知后端。
- `UIOpenOptions.BlockGameplayInput` 默认为 false。为暂停/奖励等窗口设为 true，窗口会话负责持有 Gameplay 阻断，覆盖加载和开关动画，失败/取消亦释放；排队期间不提前持有。
- UIInputBlocker 组件已移除。原 IUISystem.ShowMask/HideMask 全屏输入遮罩已移除，流程改用 `using (gate.Block(InputScope.All))`。
- UIOpenOptions.ShowMask/ClickMaskToClose 的弹窗背景遮罩仍保留，不与全局输入阻断混用。
- UI 阻断取消按压与拖动，清除编辑焦点；必要的 PointerUp/EndDrag/Deselect 清理通知仍会发生，不发送 Click/Submit/Drop。
- EventSystem 保持运行。JulyStandaloneInputModule 正常分发只调用 base.Process()，不复制按压、点击、拖动或导航流程。
- 回调内申请阻断允许当前一轮原生处理完成，随后统一收尾；取消路径不合成 Click/Submit/Drop，不回滚本轮已执行的业务。
- 解除 UI 阻断后等待触摸、鼠标按钮、导航轴及提交/取消键释放，再接收新操作；不恢复旧编辑焦点。只处理 EventSystem 交互，不接管任意控件自己轮询设备的 Update。
- 本轮完成静态迁移，未运行测试或 Unity 编译，交互行为尚待运行验收。
