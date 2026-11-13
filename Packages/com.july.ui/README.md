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
- 失焦取消当前交互，不新增全设备释放等待；恢复后允许新的鼠标按下和触摸 Began，不接管没有起始按下的旧触摸，也不解除既有 UI 阻断或释放等待。
- 回调内申请阻断允许当前一轮原生处理完成，随后统一收尾；取消路径不合成 Click/Submit/Drop，不回滚本轮已执行的业务。
- 解除 UI 阻断后等待触摸、鼠标按钮、导航轴及提交/取消键释放，再接收新操作；不恢复旧编辑焦点。只处理 EventSystem 交互，不接管任意控件自己轮询设备的 Update。
- 失焦恢复已通过隔离 Unity 2022.3.62f2 PlayMode 的六项真实 UGUI 交互回归及 July.UI 编译；完整宿主与真机仍需实际验收。

## UI shaders

All runtime UI shaders live under `Runtime/Shaders`. The move from
`Runtime/Components/Shaders` preserves shader names and `.meta` GUIDs; existing
material references and `Shader.Find` callers continue to use the same names.

### TextMeshPro double outline

Use `JulyGF/UI/TextMeshPro Double Outline` on a material preset for the target
TMP SDF font. Create the preset from that font's material first, then change its
shader, preserving the atlas texture, dimensions, gradient scale and font weights.
Fonts, atlases and project-specific material presets remain in the consuming project.
The shader declares its TMP uniforms directly and has no project-relative include.

- `Face Color` controls the original face. `Inner Outline Color/Width` and
  `Outer Outline Color/Width` control two successive outward rings. Changing ring
  width does not shrink the original face.
- Each width ranges from 0 to 0.4 in normalized SDF distance, not screen pixels;
  apparent width depends on the font atlas and text size. Zero width removes that ring.
- Hidden TMP properties reserve mesh padding for the maximum supported widths.
  Keep these properties on the shader; `_OutlineWidth` is a padding reservation,
  not a visible outline control. The font atlas also needs sufficient padding.
- The material uses premultiplied alpha. TMP vertex alpha fades the face and both
  rings together; RectMask2D, stencil Mask and UI alpha clipping are supported.
- Glyphs render separately. Thick rings can overlap neighboring glyphs; adjust
  character spacing when needed. The shader does not composite the entire text
  into a shared outline or provide the full standard TMP effects/Inspector.

When migrating a project-local copy, move its `.meta` with the shader to preserve
material references and remove the old copy when upgrading the package. Do not
keep both copies with the same GUID in one Unity project.

Validation for 0.4.2 passed 44 checks in an isolated Unity 2022.3.62f2 /
Direct3D11 project, loading an unchanged shader-only snapshot of this package
from a UPM package path: rendering of the original face, both
rings, zero/maximum widths, vertex opacity, alpha clipping and both UGUI mask types,
plus compilation/name/GUID checks for all six shaders. Source comparison also
verified that all five moved shader bodies and their metadata are unchanged.
This does not establish full July.UI assembly integration, mobile graphics-backend
or full consuming-game acceptance.


## TMP character reveal

`UITextReveal` owns `TMP_Text.maxVisibleCharacters` and temporarily disables
`useMaxVisibleDescender` while playing to keep the full text baseline stable. It never writes
`text`, translates strings, or owns a window's visibility or minimum display time.
Keep the complete text (including animated suffix characters) in the consuming
project's authored/localized content. Do not combine it with another owner of
`maxVisibleCharacters` on the same TMP object.

- `Animated Character Count`: 0 reveals the whole text; a positive value reveals
  only that many final TMP characters and keeps the preceding text visible. Playback
  starts with that range fully hidden, then reveals one character per interval.
  Shorter texts use their available characters; empty text stays empty. TMP rich-text
  tags do not count as characters.
- `Character Interval`: positive finite seconds between character steps.
- `Loop Count`: total play count; 1 plays once, positive integers play that many
  cycles, and -1 repeats indefinitely. Repeated cycles hold the fully revealed text
  for one interval before restarting; finite playback ends with the complete text.
- `Ignore Time Scale`: use unscaled time (default true).
- `Play On Enable`: automatically replay on enable (default true).

`Play()` restarts. `Stop()` stops and
restores the original visible-character setting; disabling/destroying the object
also stops. Settings are captured when playback actually begins; stopping before
playback or stopping again does not overwrite externally assigned settings.
Playback advances in LateUpdate from accumulated scaled or unscaled frame time.
A delayed frame jumps to the matching character step. No tween, coroutine or async
task is created; Stop and OnDisable stop the counter and restore owned TMP settings.
Setup occurs in LateUpdate after other components' OnEnable text
assignment, so UILocalizedText does not require a dependency or execution-order
setting. After replacing the complete source text while enabled, call `Play()` to restart.
Full text remains in TMP layout, keeping the fixed prefix in place while the suffix
is revealed. Parsed whitespace/sprites count as TMP characters too.

Examples: a typewriter uses the defaults (whole text, starting empty, once). A looping
three-character suffix uses animated count 3, interval 0.3, loop count -1;
the suffix can contain any characters, not only periods.

Unreleased validation is recorded with the consuming project's integration checks;
this section does not imply a new package tag has been published.

The component PlayMode tests require the host project to configure TMP Settings
with a default font covering ASCII digits, periods and lowercase `g`/`y`.

## Authored disabled visuals

`UIDisabledVisual` switches explicitly authored Normal/Disabled values on one UI
subtree. Add it to the UI root, then configure any number of Image sprite pairs,
child GameObject active pairs, Graphic color pairs (including TMP), and TMP shared
font-material preset pairs. Empty sprite references explicitly clear the image.
Unlisted properties, text contents, image dimensions, raycasts and gameplay stay
with their existing owners. Normal values are authored, not runtime snapshots.

- For ordinary buttons, assign the visual to `UISmartButton.Disabled Visual`.
  Continue calling `button.SetInteractable(canUse)`; initial state, repeated calls,
  updates while inactive and reopen reapply the corresponding visual. One visual
  must have one button source. Do not independently drive that visual as well.
- For a gray-but-clickable element, leave the button reference empty and call
  `visual.SetDisabled(isLocked)`. The visual never blocks input itself.
- Own each property in one place. For stock/advertisement badges, let this visual
  hide an outer container while project logic controls its children. Never bind
  the visual's own GameObject or an ancestor to its active-object list.
- Targets belong to this subtree. Do not combine overlapping UIGrayGroup,
  UISmartButtonGray/UISmartButtonSprite, animation tracks, another visual, or
  project writes to the same owned properties. Existing legacy button components
  remain available; use plain UISmartButton when adopting the new visual.
- For outlines, author two material presets for the target SDF font. The preset
  must use the font's atlas texture and a TMP SDF shader. Assignments use
  `fontSharedMaterial`; preset contents are never mutated and the visual creates
  no per-element materials. Standard and July double-outline presets are supported.
  Font changes need matching new presets; independent runtime font/icon rebinding
  is outside this component's current contract.
- Prefabs directly reference sprites/materials and use the existing UI/resource
  lifetime. No runtime address lookup, automatic gray-image naming or SO registry.

The Inspector uses four counted foldouts (empty groups start collapsed), with paired Normal/Disabled fields and a compact capture/preview toolbar. Help lives in tooltips; configuration errors remain visible. It captures current Normal values with
Undo, and previews either state. Preview uses Unity AnimationMode to restore the
actual scene values (which need not match the authored Normal values); it does not
modify the initial state, button or business data. End preview before editing.
Leaving the Inspector, saving, assembly reload, Undo and entering Play Mode stop
preview. Another active animation preview blocks starting this one. Missing,
external, duplicate and conflicting targets and incompatible font presets are
reported in the Inspector; invalid local runtime configuration fails on first use.

Disabling/reopening a window preserves the current visual state. Hiding the window
does not mean that its business condition became Normal. No automatic OnDisable
rollback is performed. Configuration is fixed for an instantiated visual.


## Art numbers

`UIArtNumber` uses authored Image slots and digit sprites (0-9). Minimum Digits
defaults to 1; set it to 2 to display 0 as `00` and 5 as `05`. Provide enough
slots for the largest number. Images retain native sprite sizing.

Layout has four options: Manual preserves authored slot positions; Left aligns
to this component's RectTransform left edge; Center centers within its rectangle;
Right aligns to its right edge. Automatic placement uses the owner rectangle's
coordinates independently of slot anchors and accounts for owner and digit pivots.
Spacing controls the gap between native-size slots in all three automatic layouts;
negative spacing is supported. Digit order remains left to right. Use unit digit
scale and scale the whole number to size the display. The container needs enough
width for the native-size digits and spacing; layout does not shrink or clip them.
Decorations such as a timer colon are placed independently.

The Inspector refreshes assigned Images immediately when display settings change.
In edit mode, Preview Number selects the value (initially 0); it does not change
CurrentNumber or the runtime value cache. In Play Mode, settings refresh the
current runtime value. Generated sprite, size, visibility and layout edits support
Undo and prefab instance overrides and can be saved as the authored appearance.
Merely selecting the component does not rewrite its children. Incomplete bindings
show a warning and skip preview until all ten sprites and the slots are assigned.

For a timer, compose two two-digit numbers with an authored colon; time rules and
unlimited-state visuals belong to the consuming project. No timer or resource
loading is added to the number component.

Migration: replace serialized `_centerAlign: 0` with `_layout: 0` (Manual), and
`_centerAlign: 1` with `_layout: 2` (Center), including any prefab property overrides.
The consuming GooseMarketSort project's four existing bindings have been migrated
explicitly. No legacy boolean field is retained.
