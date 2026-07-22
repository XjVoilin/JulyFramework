# July Framework

July Framework is a reusable Unity framework maintained as a package monorepo. The repository contains framework packages and package-level tests only; game composition belongs to the separate `Template_2022.3` repository.

## Repository layout

- `Packages/` contains independently installable UPM packages.
- `docs/adr/` records durable architectural decisions.

See [docs/package-map.md](docs/package-map.md) for package boundaries and dependency rules.
See [docs/installation.md](docs/installation.md) for copyable Git URLs and installation profiles.

## Deterministic development

One immutable framework revision identifies a compatible version of every July package. Package-level tests live beside their owning package under `Tests~`.

For distribution, publish the `Packages/com.july.*` directories to a scoped registry at one shared version, or list every required package with a Git URL pinned to the same immutable commit and `?path=/Packages/<package-id>`. Do not use unqualified branches such as `main` in a game manifest.

## Verification

- The separate `Template_2022.3` repository consumes the packages as a real Unity project and provides integration, launch, and build verification.
- Package-level EditMode and PlayMode tests remain in their owning package.
- Supported editor baseline: Unity `2022.3.62f2`.

The legacy repository snapshots remain outside this repository at `../Rep/` during migration. They are not modified by the monorepo conversion.
