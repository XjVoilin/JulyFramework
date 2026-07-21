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
