# Changelog

## 0.1.0

- Extract standard release UI, CI, pipeline steps, COS uploads, resource collectors and project-bound build helpers.
- Isolate WeChat/TikTok SDK adapters by editor assembly without repackaging vendor SDKs.
- Share CoreVersion/PlanVersion backend protocol and runtime resource URL semantics.
- Preserve project CDN/COS URL prefixes, local output layout and existing version/tag rules.
- Keep AOT checking scoped to project source/macros; package upgrades require a full build by policy.
