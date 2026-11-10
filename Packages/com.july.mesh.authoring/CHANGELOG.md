# Changelog

## Storage cleanup - 2026-11-08

- Remove unused legacy MeshEditRecord and MeshBinding types.

## Unreleased

- Keep original Mesh baselines and applied settings in Library/July/MeshAuthoring as local-only data. Clarify restore and ratio semantics; clearing Library or changing machines establishes a new baseline from the current Mesh. Preserve live Mesh references.

## Unreleased

- Share one MeshCollider reference scan within each synchronous apply/restore batch. Preserve per-mesh failures, loaded/persistent/unloaded-scene checks, scene restoration and cancellation before writes or during scanning; standalone backend edits still check their own references.

- Skip normal application when candidate and current triangle counts match, including buffer-only or settings-only changes; show the no-change state and count the item as skipped. Triangle increases and explicit baseline restoration remain supported.

## 0.1.4 - 2026-10-25

- Route mesh-authoring failures through JLogger; keep cache diagnostics available independently of JULYGF_DEBUG.

