# Separate framework and template repositories

July Framework and `Template_2022.3` have different release and maintenance responsibilities. The framework repository owns reusable UPM packages and package-level tests. The template repository owns the Unity project, composition root, project settings, fixed third-party tooling, and integration verification.

## Status

Accepted, 2026-07-21.

## Consequences

- Framework consumers do not clone a complete Unity project.
- The template exercises the framework through the same package seam used by a real game.
- Package tests remain local to their implementation; cross-package launch and build checks run in the template.
- Local development may use repository-relative `file:` dependencies.
- Published templates must replace local paths with a scoped-registry version or Git dependencies pinned to an immutable framework revision.
