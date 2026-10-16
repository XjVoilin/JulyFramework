# July Physics Authoring

Editor-only Box, Sphere and Capsule fitting for Unity 2022.3. Version 0.3.0 accepts a selected output Transform instead of a Collider. Version 0.2.0 replaced the former convex Mesh output. The package has no native plugin and creates no Mesh objects or Mesh asset files. Prefabs use Unity primitive Colliders at runtime.

## Use

In Prefab mode, select the dedicated collision parent in the Hierarchy and choose **JulyGF → 根据 Mesh 生成碰撞体…** from its right-click menu (also under GameObject). The parent needs only a Transform, and may be empty. `ColliderAuthoringWindow.Open(outputTransform)` accepts any name; it never finds a group by a fixed name or a Collider component. Persistent prefab-group Transforms open the corresponding node in Prefab mode. Scene prefab instances must be edited in Prefab mode; ordinary scene objects can be edited directly.

The panel scans display MeshFilters. Sources are collapsed by default; expand to replace/add/remove Mesh and coordinate-node pairs or rescan. The selected parent is the dedicated output group. Changing it in the panel also reloads sources and group defaults. Set **贴合误差** and **最大形状数**, then generate. Sources/submeshes are combined into one object-wide budget. Display meshes are read with Editor MeshData access; their Read/Write setting and geometry remain unchanged.

Generated `JulyShape_Box_000`, `JulyShape_Sphere_001`, or `JulyShape_Capsule_002` children are independent for normal Transform/Collider editing. The selected parent and shape children may be renamed without affecting targeting. Existing groups supply their shared Collider settings; groups containing different settings must be split or normalized before opening. Empty groups use the parent layer and default non-Trigger/enabled settings. The package does not infer click/physical purpose or depend on a child Collider to open. Sources default to the nearest owning Rigidbody, or the current Prefab root; project Editor code may provide an authoritative display reference.

Generation replaces Colliders directly on the output node and all children. The node must be dedicated to shapes and must not contain Renderer or Rigidbody components. The output transform and ancestors require uniform scale without shear; source display transforms may be mirrored or nonuniform. Replacement and project callbacks form one Undo group; callback failure restores the previous result. No asset is saved implicitly. Save the prefab or scene after editing.

## Fitting and controls

Geometry is conservatively rasterized into voxels, with 32 cells along the longest dimension. Exterior flood fill estimates the occupied volume across all sources, preserving holes larger than this resolution. Candidate oriented Boxes, Spheres and Capsules enclose voxel samples. A bounded search retains four alternative split plans, based on principal axes and cross-section density changes. It chooses fewer shapes when the fit is adequate, and only accepts extra shapes when the complete plan measurably reduces estimated excess volume.

- **最大形状数** defaults to 6, range 1–16, for the entire object.
- **贴合误差** defaults to 0.4, range 0.05–1. It is the estimated excess covering volume relative to voxel occupied volume, not a screen-space or exact geometric error guarantee. Lower values can require more shapes. Conservative voxel enclosure and overlapping primitive volumes can overestimate this ratio.

Generation returns the best plan found within the budget. `MeetsTolerance=false` means it still needs review/manual adjustment or a larger budget; it is not labeled as having met the target. Tiny holes, narrow branches, open meshes and features near the voxel size need manual inspection. This is a deterministic approximation, not a globally optimal decomposition or a guarantee of click accuracy.

## Calling from project tools

`ColliderGeometry.Scan(root)` collects editable sources. `await ColliderGeneration.GenerateAsync(request)` fits and replaces one group and returns `Collider[]`, estimated excess coverage and whether the requested tolerance was reached. This same method is used for sequential batch operations. There are no mesh-output-path, hull-vertex, native-platform, sampling or seed settings.

`ColliderAuthoringWindow.Open(Transform)` opens one explicit replacement group, including an empty one. `ColliderAuthoringWindow.ContextOpening` receives SourceRoot and Request (no Collider dependency), allowing project Editor code to choose display sources and Collider properties. `ColliderGeneration.Applied` runs inside the Undo transaction so project code can configure its components and references through Undo/SerializedObject. The package does not depend on game types or runtime lifecycle/event services.

The panel edits one serializable `ColliderGenerationRequest` directly. Callers must leave the request unchanged until `GenerateAsync` completes. Mesh inputs are validated when read; the editable output is checked again after background fitting because the user may close Prefab mode or change the scene during that time. Resource folder creation belongs to the consuming prefab tool.

Only the local Editor panel's most recent parameters are remembered, using a new preference key independent of version 0.1. No per-prefab authoring component or parameter asset is created.

## Existing content and tests

Existing prefabs are not rewritten on import. Explicit regeneration converts the selected group to primitive shapes and replaces hand edits. Previous collision Mesh files are neither modified nor automatically deleted, because other assets or Undo may still reference them. Clean up confirmed unused assets separately.

With Unity Test Framework installed, add `com.july.physics.authoring` to `testables` and run EditMode tests. Tests cover automatic Box/Sphere/Capsule choice, separated meshes with one shared budget, ring-hole picking, unreadable meshes and mirrored transforms, no new Mesh objects/assets, Undo/Redo, callback rollback, invalid output scale protection, empty/renamed/inactive group entry, sibling scope preservation, and mixed group rejection. Game-specific integration tests belong to the consuming project.
