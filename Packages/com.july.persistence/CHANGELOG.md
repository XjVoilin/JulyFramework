# Changelog

## 0.3.1 - 2026-08-13

- Moved the shared LitJson binary to the independent `com.july.json` package.

## 0.3.0 - 2026-07-22

- Added an explicit platform preferences adapter seam with Unity PlayerPrefs as the default.
- Added a conditional TikTok adapter backed by ByteGame's synchronous TTStorage API.
- Removed the invalid direct dependency on `TTSDK.TT.PlayerPrefs` from the core runtime assembly.

## 0.2.0

- Added platform-aware preferences and reusable savable store lifecycle support.
