# Use one package monorepo

July Framework is maintained in one Git repository with flat, independently installable UPM packages under `Packages/`. Package seams follow independent installation and dependency needs; namespaces and asmdefs provide finer internal separation. The template is maintained in a separate repository, while optional third-party adapters such as YooAsset and Spine remain separate packages without an `Integrations` container.

## Status

Accepted, 2026-07-21. The original shared-revision distribution consequence is superseded by ADR 0003.

## Consequences

- Packages can still be selected independently through UPM.
- Cross-package changes are reviewed atomically and package tests stay with their owning packages.
- Game-specific policy and integration verification remain in the separate template repository.
- External distribution uses the independent package release policy in ADR 0003.
