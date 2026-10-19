# Changelog

## 0.2.0 - 2026-10-19

- Add StartPreDownloads(tags), PausePreDownloads(exceptTags), and ResumePreDownloads()
  to IResourceSystem; custom implementations must implement the new contract.
- Keep ordinary tag downloads as the readiness API and ResourceScope unchanged.
- UnityResourceSystem implements the commands as no-ops because Resources assets
  do not require network downloads.
- Update resource and dependent UI/Scene test implementations for the interface.
