# Changelog

## Unreleased

- Apply the shared file and recursive-folder ignore list to both copy scanning and execution; invalidate copy results when ignores change, show ignored counts, and reject externally changed configuration before further writes.
- Consolidate checking and platform-copy tools in one Resource Tools window with a shared persisted folder, independent results and actions, task exclusivity, and host-owned panel cleanup.
- Add explicit folder selection and read-only scanning before copying; show per-file WebGL and target configurations, difference filtering, checkboxes, and asset navigation. Require fresh complete results, stop on relevant asset changes, and rescan after copying.
- Add an independent WebGL platform-settings copy window for imported textures and SpriteAtlas assets, targeting MiniGame and WeixinMiniGame without applying checking standards.
- Copy effective defaults when WebGL overrides are disabled; preserve Automatic and all platform parameters. Batch imports, skip unchanged assets, and allow cancellation between batches.

## 0.7.1 - 2026-10-25

- Route resource-checking errors and exceptions through JLogger and declare the July Logging dependency.

