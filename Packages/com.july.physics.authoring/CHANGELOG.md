# Changelog

## 0.4.0 - 2026-10-19

- Add the runtime ColliderAuthoringTarget marker and its Inspector-based authoring workflow, including empty, inactive and renamed output groups.
- Move project integration to ColliderAuthoringContext.Create, Opening and LastSettings; remove ColliderAuthoringWindow and its menu entry points.
- Preserve source selection across Undo/Redo, share in-progress Inspector state and keep generation plus project callbacks in one rollback-capable Undo operation.
- Extend Editor regression coverage for the marker, mixed settings, context and regeneration behavior. Preserve existing component meta GUIDs when migrating from embedded packages.

## 0.3.0

- Existing independently tagged release with window-based primitive collider authoring.
