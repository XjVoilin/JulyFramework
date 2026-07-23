# July Framework

July Framework is a reusable Unity framework maintained as a package monorepo. The repository contains framework packages and package-level tests only; game composition belongs to the separate `Template_2022.3` repository.

## Repository layout

- `Packages/` contains independently installable UPM packages.
- `docs/adr/` records durable architectural decisions.

See [docs/package-map.md](docs/package-map.md) for package boundaries and dependency rules.
See [docs/installation.md](docs/installation.md) for copyable Git URLs and installation profiles.

## Deterministic development

Each July Package has its own semantic version in `package.json` and an immutable Git tag named `com.july.<name>@<version>`. Package-level tests live beside their owning package under `Tests`.

Git consumers list every required package and its July dependency closure explicitly, with each URL pinned to that package's release tag and `?path=/Packages/<package-id>`. A package update does not force unrelated packages to change version. Interface changes that affect dependants must release those dependant packages together. Do not use unqualified branches such as `main` in a game manifest.

## Verification

- The separate `Template_2022.3` repository consumes the packages as a real Unity project and provides integration, launch, and build verification.
- Package-level EditMode and PlayMode tests remain in their owning package.
- Supported editor baseline: Unity `2022.3.62f2`.

The legacy repository snapshots remain outside this repository at `../Rep/` during migration. They are not modified by the monorepo conversion.
