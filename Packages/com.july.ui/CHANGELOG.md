# Changelog

## 0.2.24 - 2026-08-21

- Add configurable render texture scale, maximum render frame rate and MSAA to `UIModelPreview`, with runtime overrides reflected in the Inspector.
- Use a 16-bit depth target with the focused preview camera clipping range after device verification.

## 0.2.23 - 2026-08-21

- Prevent animated model parts from flickering in `UIModelPreview` by using a 24-bit depth target and concentrating the preview camera clipping range around the model plane.

## 0.2.21 - 2026-08-21

- Add a `UIModelPreview.ShowAsync` overload that writes the supplied scale and vertical offset directly to the preview instance, keeping the live Inspector editable and truthful.
- Keep model sizing uniform per preview instance; individual targets no longer carry layout overrides.

## 0.2.20 - 2026-08-21

- Allow each `ModelPreviewTarget` to optionally override the preview scale and vertical offset while retaining the `UIModelPreview` defaults when omitted.

## 0.2.19 - 2026-08-20

- Configure the shared model origin and horizontal spacing on each `UIModelPreview` instance instead of passing a `RectTransform` anchor for every model.
- Move display sizing from `ModelPreviewTarget` to an Inspector-adjustable `UIModelPreview` overall scale, preserving the models' original relative sizes.

## 0.2.18 - 2026-08-14

- Make `WebImage` public operations safe before `Awake`, including on inactive UI objects.

## 0.2.17 - 2026-08-14

- Add the reusable anti-aliased `UI/RoundedRect` shader with adjustable roundness and inset.

## 0.2.16 - 2026-08-14

- 修复 `FixedHandleScrollRectEditor` 在字段重命名后无法找到序列化属性的问题。

## 0.2.15 - 2026-08-14

- 统一私有字段命名为 `_camelCase`，并通过 `FormerlySerializedAs` 保留现有 UI 序列化数据兼容性。

## 0.2.14 - 2026-08-14

- Let `UIToggleGroup` obtain a fresh optional `ProcedureBase` from a project-provided factory for every selection.
- Unify immediate and prepared selections behind `SelectAsync` and remove the external manual-commit protocol.
- Add `EnsureUIContentProcedure` for idempotent lazy UI content instantiation.

## 0.2.13 - 2026-08-14

- Allow `UIToggleGroup` to await an optional one-shot `ProcedureBase` before committing a selection.
- Cancel pending asynchronous selections when a newer or synchronous selection supersedes them.

## 0.2.12 - 2026-08-12

- Allow each model preview target to configure its instantiated model.
- Allow callers to cancel model preview loading.
