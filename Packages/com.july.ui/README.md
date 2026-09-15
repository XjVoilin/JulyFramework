# July.UI URP camera composition

UI 0.2.28 requires July.Scene 0.1.1 and URP 14.0.12 or a compatible newer version.

UISystem owns the persistent UI camera and its composition lifetime. No project-level
CameraStackHandler, CameraStackBinder or per-scene preparation call is required.
Use ISceneSystem for runtime scene loading and unloading. UI subscribes to events only;
it does not require SceneSystem to initialize before UISystem.

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
