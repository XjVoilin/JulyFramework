# Changelog

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
