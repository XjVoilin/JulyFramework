# Changelog

## Unreleased

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
