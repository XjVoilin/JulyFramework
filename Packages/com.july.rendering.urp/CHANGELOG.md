# Changelog

## 0.2.1 - 2026-10-20

- Match the silhouette vertex projection to ordinary URP surfaces (world xyz, then constant-w projection).
- Bias only the visible mask by one depth unit, without slope bias, to tolerate same-surface rounding differences between independently compiled passes. Do not write scene depth or change the complete silhouette/composite.
- Add a rendered depth regression: a one-unit difference must remain visible, while a 64-unit depth separation must still be occluded. iPhone red/green diagnostics confirmed self-occlusion in 0.2.0; this patch still requires device retesting.


## 0.2.0 - 2026-10-20

- Replace shared-stencil directional geometry with complete/visible RG8 masks and one local composite; preserve component/Shader GUIDs and material parameter names.
- Project individual renderer bounds and discard off-screen regions before union to reduce oversized composite rectangles.
- Smooth animated outer glow using linear coverage sampling while retaining point-sampled silhouette, outline and occlusion rules.
- Require orthographic Base cameras, MSAA=1, a renderable RG8 attachment and addressable scene depth; reuse original static meshes without material/mesh clones or scene-color copies.
- Release mask resources on feature recreation/disposal/disable. Keep selection cancellation and camera-local lifecycle contracts.
- Validated PC behavior, animated edge coverage and GLES3 compilation; WeChat iOS/Android device acceptance remains pending. Rebuild the Player/AOT code and resources when upgrading.


## 0.1.0 - 2026-10-19

- Initial reusable orthographic URP static-mesh highlighting, extracted from the validated Game110 effect.
- Camera-local candidate, compound static-mesh target, shared stencil stages, instanced direction draws, and configurable shared-material shader.
