# Changelog

## Unreleased

- Align PS/Figma export schema v2: fractional layout, raster-only *N, single opacity application, explicit Tint, and text alignment.
- Share translated sprite references with Art Importer; import PNGs as Single Sprites and preflight missing files/output paths before copying.
- Return actual generation/cancellation results and Prefab paths; preserve Prefab GUIDs and clean temporary objects on failure.
- Preserve Slider Fill Tint/opacity and add EditMode/cross-tool regression coverage.

## 0.2.1 - 2026-11-11

- Remove only the Project Settings > July > UI制作 entry; preserve existing settings storage, default paths, and JSON context-menu Prefab generation.

## 0.2.0 - 2026-11-10

- Store PS2UGUI settings in ProjectSettings/July/UIAuthoring and edit them through Project Settings; stop creating/searching settings assets.

## 0.1.3 - 2026-10-25

- Use JLogger for UI import/generation errors with explicit dependencies; keep authoring information and warnings independent of JULYGF_DEBUG.

