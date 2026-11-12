# Changelog

## Unreleased

- Browse saved client resource reports independently of the active Unity build target. Add an explicit report-platform selector, default to the newest available resource data, retain the open-page selection on refresh, and discover legacy records without switching platforms or building.

- Use a concrete, script-backed EditorWindow for Player resource UI regressions instead of showing the EditorWindow base type; keep explicit Close cleanup and avoid anonymous test-window restoration.

## 0.8.5 - 2026-11-12

- Preserve available client resource details when a successful incremental build returns no resource data. Keep every displayed metric, timestamp and output path bound to its source report; show the latest content-build status separately.
- Retain a clean-build baseline independently from the latest populated incremental report, with a direct view switch. Label incremental and legacy coverage explicitly rather than merging records or treating non-empty data as complete coverage.
- Store snapshots per build target under July/AssetCheck/PlayerBuild. Read existing Library snapshots without rewriting them or inventing coverage; migrate preserved data on the next successful content report.
- Ignore failed and scripts-only/AOT temporary Player reports. Keep callback-time resource capture and post-build finalization, existing size aggregation, thumbnails and locating.
- Add focused Editor regressions for empty/partial/clean transitions, report provenance, platform isolation, legacy records, failed/temporary builds, invalid caches and page switching.

## 0.8.4 - 2026-11-12

- Keep one pending Player build report, capture resources during the callback and finalize metadata after building. Persist successful reports even when resource details are absent; replace stale rows and show unmeasured values rather than borrowing an earlier build's details.
- Show the report's output path directly and distinguish reported resource bytes from final platform package size.
- Refine platform configuration copying with a scan header, compact metrics, collapsible rules, grouped actions, resource thumbnails, highlighted differences and a fixed status footer. Preserve copying and cancellation behavior.
- Separate bundle-report import, history and build controls; refine result tabs, tables, comparison cells and independently scrolling details in both editor themes, with compact spacing for shorter windows.

## 0.8.3 - 2026-11-11

- Add a compact client-build resource-size page with descending packed-resource sizes, search, GUID-based locating, build metadata and explicit uncompressed/build-size scope in both editor themes. Keep the latest populated snapshot in Library and existing YooAsset analysis unchanged.
- Capture every client-build callback and retain resource data until build completion. Prevent empty incremental reports from replacing populated snapshots; explain when a first Clean Build is required and display timestamps in the local timezone.
- Reuse resource-check thumbnails for visible build-resource rows, including textures, asynchronous Unity previews and native icons. Reset preview bindings when rows are recycled, and keep historical sizes separate from current-project previews.
- Align table cells and keep search actions visible when the window resizes.

## 0.8.2 - 2026-11-11

- Refine the font-check workspace with independently scrolling configuration/results panes, narrow-window stacking, persistent actions/status, scoped dark/light styles and clearer per-font/source/empty/stale states. Checking behavior is unchanged.

- Extend read-only font checks with GUID-based Assets folders for Prefab and scene TMP text, effective component fonts, inactive/nested/variant/instance text, object locations and per-font missing-character evidence. Keep JSON character-file checks separate.
- Handle common serialized TMP markup without rendering or populating font assets; report unresolved font/style/case switches, missing fonts and unsaved content as unverified. Clean up temporary content before yielding and preserve the active scene/selection.
- Invalidate results on asset/dependency/scope changes, preserve source evidence when merging characters, and clear old scan errors after a successful retry. Add focused EditMode regressions; execution remains pending.

## 0.8.1 - 2026-11-11

- Move reusable report and inspection snapshots to project-root July/AssetCheck. Keep view/selection state in Library, restore defaults without personal state, and preserve read-only shared evidence across local scope changes. Archive report names without workstation-specific source paths.

- Separate bundle-content actions: resource names select and Ping the current GUID-matched asset without navigating; a dedicated inspection button reveals its resource-check row with the owning-bundle size context and clears obstructing display filters.

- Add a Needs Attention filter as the exact complement of Passed, collecting problems, unverified/incomplete checks and optimization candidates without duplicating files or relabeling their statuses.

- Filter bundle resource results by a recorded Assets/Packages directory (including descendants), with Project selection and clear actions. Scope category counts consistently, combine with existing filters, and retain each report's directory in view state without rescanning or applying ordinary exclusions.

- Store reusable reports and per-report history under July/AssetCheck; retain personal view state under Library/July/AssetCheck.

- Archive each report by content identity with its latest complete inspection, original report bytes, size data and separate browsing state. Add history selection/recheck and baseline-to-target package/resource comparisons, including dependency closures, physical-copy sizes and per-target issue evidence. Preserve complete results on cancellation/failure and report unavailable measurements or differing comparison contexts explicitly. Reuse the ordinary issue snapshot format.

- Let analysis detail links size naturally under the shared resource-window stylesheet, preventing expanded resource/generated sections from overlapping. Cover shared styles, both themes, scrolling, repeated expansion and package changes.
- Select an installed Unity build target for analysis. Persist one editor-session request across platform compilation/reload, automatically build and import its report once ready, and retain the selected platform. Support cancelling pending work and report switch/compilation failures without invoking release preparation.
- Reuse Release's isolated AB build entry and report-bound size snapshot; add an explicit build-and-analyze action without invoking release preparation, builtin/CDN copy or player export.
- Extend the shared result table with numeric resource-size sorting, per-bundle sizes and aggregate physical-copy sizes. Keep partial/unknown measurements explicit, preserve grouping and report snapshots, and retain ordinary repair/exclusion behavior.
- Extend the existing resource results view/model and catalog type tabs for bundle inspections. Show inline rule evidence, previews and sorting with an additional owning-bundles column; keep AB-specific association/duplicate details separate.
- Record per-module applicability, issues and completion; preserve unsupported and missing entries, compound-resource categories, partial cancellation and GUID-safe snapshot identity. Do not install repair/ignore actions in bundle results; preserve ordinary scan behavior.

- Replace report browsing modes with package results and resource inspection results. Start once to analyze the full report, then inspect its current project resources and duplicate source content with progress/cancellation.
- Add configurable package size marks (32 KiB minimum / 3 MiB maximum), unique bundle dependency totals, cycle paths and report-proven resource association evidence. Preserve report facts and inspection snapshots after subsequent project edits.
- Reuse catalog modules, discovered rules and shared standards through a common per-resource execution entry. Ignore ordinary scope/exclusions for report inputs without changing ordinary reports, scope, exclusions or repair permissions. Keep all applicable subasset/module checks.
- Add read-only Sprite/atlas mipmap standards (disabled by default) and per-Mesh submesh limit (4), configurable in existing standards. Migrate v4 configuration to v5 while preserving previous standards and ignore metadata.
- Keep cross-bundle duplicate findings distinct from identical content in different source files; no estimated savings or automatic fixes. Replace old scope-filtered report UI tests with complete-snapshot coverage while retaining historical exclusion regressions.
- Read report dependencies as flat identity records, matching YooAsset and avoiding recursive Unity serialization warnings.
- Validate 240 EditMode tests, including cancellation between rule and content phases and inherited variant-atlas mipmaps, explicit path matching, and toolbar visibility; compare the real 442-bundle report without changing its bytes.

## 0.8.0 - 2026-11-10

- Remove obsolete configuration upgrades and old report schema support; current rules and report formats remain unchanged.

- Store shared rules under ProjectSettings/July/AssetCheck; keep the last scan scope in Library/July/AssetCheck without rewriting shared rules.

- Replace the transient font-source ObjectField with an Add JSON File button. Show one deduplicated missing-character list and clipboard action, retain concise character-file/font guidance, and provide collapsed per-file missing-character details and asset location actions.

- Add a read-only font-check page: discover Assets language JSON sources, persist editable GUID lists and exclusions, compare all decoded string-value characters with a selected character JSON and the TMP default font, show source evidence and copy deduplicated missing Unicode characters. Keep scans cancellable, exclusive and stale-aware without changing business tables or populating font atlases.

## 0.7.1 - 2026-10-25

- Route resource-checking errors and exceptions through JLogger and declare the July Logging dependency.

