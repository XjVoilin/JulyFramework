# Use independent package release tags

July Packages are released independently even though their source remains in one Git monorepo. Each package declares its own semantic version in `package.json` and is published with an immutable Git tag using this format:

```text
com.july.<name>@<version>
```

For example, `com.july.resource.yooasset` version `0.2.3` is selected with:

```text
https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource.yooasset#com.july.resource.yooasset@0.2.3
```

## Status

Accepted, 2026-07-22. Supersedes the shared-revision distribution consequence in ADR 0001.

## Consequences

- Updating one package does not force unrelated packages to receive the same version.
- A tag version must equal the selected package's `package.json` version.
- A breaking or interface-affecting change must also release every dependant package that needs a corresponding change.
- Git-based consumers explicitly list the selected package and its July dependency closure because UPM cannot infer sibling `?path=` dependencies in a monorepo.
- Game manifests never depend on mutable branches such as `main`.
- The template repository is the integration check for a concrete set of package versions.
