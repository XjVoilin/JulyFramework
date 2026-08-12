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
