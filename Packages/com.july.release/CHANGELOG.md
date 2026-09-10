# Changelog

## 0.1.4

- Add explicit full-build AOT output and hot-update AOT input paths, with strict entry-point and path validation.
- Publish immutable AOT archives with platform, BuildTarget, CoreVersion, required assemblies and SHA-256 file inventory; reject incomplete or corrupt archives.
- Restore the explicitly selected baseline even when a workspace exists, assert an optional requested backup version, and prohibit automatic selection or legacy fallback for explicit inputs.
- Preserve local archive/restore behavior when paths are omitted, without changing the build runner or publishing pipeline.

## Unreleased

- Simplify build UI into build, result and collapsed maintenance sections; retain custom steps and share AOT baseline selection.
- Use one selection for preview and execution; refresh asset-backed values and context-specific version queries.
- Restore the original player version in a finally block after QA builds; target local CDN tools at the selected environment/platform/version.
- Make FullBuild upload=false suppress data/preload uploads too, retaining local preload generation and game.js injection.

- Move BuildConfig and the build menu into release; discover the project asset lazily and let platform SDK adapters register themselves.
- Replace project Configure bindings with the runtime IReleaseBootConfig contract and Inspector settings; this is a breaking integration change.
- Share package name, resource tags and supplemental AOT assembly policy between build and runtime through the boot asset.

- Resolve CoreVersion for CI single-step runs and selected AOT baselines in the hot-update panel.
- Query live PlanVersion using the build context CoreVersion; reject contexts without a CoreVersion before executing steps.

## 0.1.0

- Extract standard release UI, CI, pipeline steps, COS uploads, resource collectors and project-bound build helpers.
- Isolate WeChat/TikTok SDK adapters by editor assembly without repackaging vendor SDKs.
- Share CoreVersion/PlanVersion backend protocol and runtime resource URL semantics.
- Preserve project CDN/COS URL prefixes, local output layout and existing version/tag rules.
- Keep AOT checking scoped to project source/macros; package upgrades require a full build by policy.
