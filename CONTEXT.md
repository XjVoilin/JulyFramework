# July Framework

July Framework is a reusable Unity seed framework distributed as independently installable packages from one source repository.

## Language

**July Package**:
An independently installable UPM distribution unit with one explicit responsibility and dependency set.
_Avoid_: Repository, component

**Template Repository**:
The separate Unity project repository that selects and composes July Packages for a new game.
_Avoid_: Demo project, aggregate package

**Domain Package**:
A July Package that owns one reusable game or runtime domain such as resources, localization, tasks, or UI.
_Avoid_: Common package, toolkit

**Adapter Package**:
An optional July Package that connects a Domain Package to a third-party runtime, such as YooAsset or Spine.
_Avoid_: Integrations folder

**Composition Root**:
The Template Repository code that chooses implementations, registers systems, and assembles the launch pipeline.
_Avoid_: JulyGame package, global container

**Store**:
A domain-state owner whose data source is selected by the Composition Root rather than by its inheritance hierarchy.
_Avoid_: Savable Store, server Store

**System**:
A long-lived runtime module that owns one capability and is ready for dependants when its initialization completes.
_Avoid_: Manager, service

**Procedure**:
A one-shot workflow that coordinates runtime capabilities for a business operation.
_Avoid_: Initialization phase, lifecycle hook

**Persistence Declaration**:
The Composition Root's explicit choice that a Store participates in local restoration, dirty tracking, and saving.
_Avoid_: Savable Store registration, persistence mode

## Editor caches

Reusable Editor computation caches owned by July Packages belong under the consuming Unity project's `July/<Feature>/`, directly beside Assets. Organize by feature, without Settings/Records/Cache category roots. AssetCheck report/inspection snapshots and ColliderAuthoring successful candidates may be committed with the project. ColliderAuthoring reproducible analysis/raw-fit stages are an explicit local-cache exception under Library/July/ColliderAuthoring/PickingProxyStages; they are regenerated as needed and are not shared candidate records. Personal view state, recent selections/paths, temporary jobs, logs and machine-specific tool dependencies remain under `Library/July/<Feature>/`. Do not migrate configuration assets, recipes, author reviews, output ownership manifests, build/AOT archives or runtime data. MeshAuthoring baselines retain historical source geometry for restoration and remain in Library; they are not reproducible computation caches. Unity and third-party caches retain their own locations. Build outputs and their report/size files remain together in their build output directories. Ignore `July/**/*.tmp` and `July/**/.pending_*/`; do not ignore the whole July directory.

Shared cache identity must survive changing the project path and recompiling on another workstation. Use stable resource/content identities, computation parameters and algorithm/format revisions rather than absolute paths, InstanceIDs, modification times or assembly MVIDs. Viewing shared snapshots only writes local view state; missing local state is normal. Shared reports remain historical evidence, not permission to mutate current resources. Configurations and author records retain their existing validation and ownership.

## Configuration authoring

`com.july.config.authoring` owns Editor-only Luban execution and managed output publication. Configuration lives in Project Settings; input follows Tools/Luban/DataTables. Projects extend ordinary C# generation schemes and output processors. Custom panels inherit ConfigAuthoringWindow and expose their own MenuItem. Do not introduce ScriptableObject scheme/panel assets or another generation pipeline. The generation window only contains generation actions and settings access; it has no path editors, refresh or log controls.

## Physics authoring

`com.july.physics.authoring` 0.6.0 is distributed as an Editor-only UPM package under its independent immutable tag com.july.physics.authoring@0.6.0. The package was synchronized from the validated GooseMarketSort embedded package on 2026-11-11. Envelope output is native Box/Sphere/Capsule; picking structure prefers native shapes then budgeted convexes. Manual reuse is a trusted snapshot and does not run generation-quality acceptance. Recipes/reviews remain in ProjectSettings; successful candidates are directly under July/ColliderAuthoring, while intermediate stages and personal state are in Library.

The obsolete marker/recipe components and runtime assembly were removed. Consumers must clean old serialized metadata and assembly/linker references before upgrading; preserve active colliders and mesh data. Projects reference only July.Physics.Authoring.Editor from their Editor integration. See package README for upgrade boundaries and public APIs.
