# Changelog

## 0.2.3 - 2026-07-22

- Treat collector group descriptions as project-owned, non-functional metadata.
- Preserve existing descriptions while validating and repairing functional collector settings.

## 0.2.2 - 2026-07-22

- Respect `UpdateManifestAfterInitialization` in every play mode, including Editor Simulate.
- Restore the active-manifest invariant before asset queries and tag-based downloads.

## 0.2.1 - 2026-07-22

- Validate complete collector definitions instead of checking group names only.
- Repair stale group metadata, rules, collector paths and GUIDs idempotently.

## 0.2.0 - 2026-07-22

- Added idempotent early initialization for pre-Arch hot-update flows.
- Reused an existing YooAsset package instead of creating a duplicate package.
- Added conditional WeChat and TikTok mini-game filesystem adapters.
- Added SceneManager fallback when unloading scenes not loaded through YooAsset.
- Clarified the ownership split between framework mechanics and project policy.

## 0.1.0

- Added the initial YooAsset implementation of `IResourceSystem`.
