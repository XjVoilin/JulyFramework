# Changelog

## 0.6.0 - 2026-11-11

- Ship as an Editor-only UPM package. Remove obsolete authoring marker/recipe components and the runtime assembly; keep project integration in Editor adapters. Existing projects must clean old serialized components and assembly/linker references before upgrading.
- Use native-only envelope fitting and picking-oriented structure generation, with primitive-first output, per-item budgets and manual reuse. Keep approved geometry, sampling, fit order and generated asset identities.
- Preserve direct current-to-envelope and envelope-to-structure snapshots without generation-quality acceptance; store manual provenance and keep manual candidates independent of generation parameter edits.
- Replace the single-column performance dropdown with multiple current/candidate scopes, explicit severity buttons and a separate all-convex condition; deduplicate item summaries and align them with search/context filters.
- Remove unused regular-shape candidate search and the diagnostic Scene window. Stop retaining transient policy grids and full optimizer/training objects in standalone results; reuse active-candidate arrays and avoid per-row filter key/enumerator allocation.
- Consolidate atomic document/cache IO, allow delete-sharing readers and bound retries to known transient Windows replace conflicts. Keep persistent errors observable and retain previous files on failure.
- Store candidates directly in July/ColliderAuthoring/<PrefabGUID>.json and disposable analysis/raw-fit stages under Library/July/ColliderAuthoring/PickingProxyStages. Keep recipes/reviews in ProjectSettings and personal state in Library.
- Verify the package through a packed UPM installation, project compilation, CPU/GPU regressions and resource migration audits before publication.

## 0.5.0 - 2026-11-08

- Retire the marker-based authoring workflow and its custom Inspectors. Resolve outputs through profiles/adapters; preserve the overview and move shared Scene preview drawing out of the removed Inspector.

- Audit every source, shader, style and importer. Remove unused third-party geometry DLLs, obsolete local documentation, dead helper methods, hidden unused queue filters and write-only experiment statistics. Stop calculating candidates discarded by the extra-primitive adapter; preserve candidate order and exact final geometry.
- Keep one generation-mode control in settings, retain serialized legacy data/GUIDs and supported public extension contracts.

- Allow generic apply to create configured output groups alongside existing colliders. Preserve outside shapes and show an informational notice; keep project adapter ownership checks and target-only replacement.

- Remove the obsolete hemisphere/cylinder segment menu, manual editing, source synchronization and unused gizmo/Mesh-write helpers. Preserve serialized recipe GUIDs and read-only legacy source recognition; existing collider/Mesh references remain untouched.

- Consolidate the approved six-family CPU/GPU generator and standalone prefab workbench in this package. The previous three-family MaxShapes/Tolerance API and its tests are superseded by TargetCount/Goal and the six-family regression suite.
- Add configurable output targets, one-target defaults, project adapters, shared overview UI and configurable project Mesh output. Remove project name/layer inference from generic generation and manual authoring.
- Keep candidate caches and durable review history separate; read legacy slot-based records through explicit target mappings without eager rewriting. Preserve existing Mesh GUIDs and content-addressed references.
- Use explicit before-save project binding callbacks; retain observable Applied events. Keep all generation and saving operations in the shared workspace.


## 0.4.0 - 2026-10-19

- Add the runtime ColliderAuthoringTarget marker and its Inspector-based authoring workflow, including empty, inactive and renamed output groups.
- Move project integration to ColliderAuthoringContext.Create, Opening and LastSettings; remove ColliderAuthoringWindow and its menu entry points.
- Preserve source selection across Undo/Redo, share in-progress Inspector state and keep generation plus project callbacks in one rollback-capable Undo operation.
- Extend Editor regression coverage for the marker, mixed settings, context and regeneration behavior. Preserve existing component meta GUIDs when migrating from embedded packages.

## 0.3.0

- Existing independently tagged release with window-based primitive collider authoring.
