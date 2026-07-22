# Installing July packages

July packages are stored under `Packages/` in one Git repository. A Git dependency selects both a package directory and an immutable repository revision:

```text
https://github.com/XjVoilin/JulyFramework.git?path=/Packages/<package-id>#v0.3.0
```

- `?path=` selects one UPM package from the monorepo.
- `#v0.3.0` pins the complete framework snapshot used to resolve that package.
- GitHub directory URLs such as `/tree/v0.3.0/Packages/...` are browser URLs and cannot be passed directly to UPM.

Paste a complete URL into Unity Package Manager through **Add package from git URL**, or add it to `Packages/manifest.json`.

## Copyable package URLs

| Package | Git URL |
| --- | --- |
| `com.july.activity` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.activity#v0.3.0` |
| `com.july.analytics` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics#v0.3.0` |
| `com.july.analytics.thinkingdata` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics.thinkingdata#v0.3.0` |
| `com.july.animation` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.animation#v0.3.0` |
| `com.july.animation.spine` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.animation.spine#v0.3.0` |
| `com.july.arch` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.arch#v0.3.0` |
| `com.july.audio` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.audio#v0.3.0` |
| `com.july.build` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.build#v0.3.0` |
| `com.july.config` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.config#v0.3.0` |
| `com.july.diagnostics` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.diagnostics#v0.3.0` |
| `com.july.events` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.events#v0.3.0` |
| `com.july.experiments` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.experiments#v0.3.0` |
| `com.july.fsm` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.fsm#v0.3.0` |
| `com.july.guide` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.guide#v0.3.0` |
| `com.july.input` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.input#v0.3.0` |
| `com.july.launch` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.launch#v0.3.0` |
| `com.july.localization` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.localization#v0.3.0` |
| `com.july.logging` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.logging#v0.3.0` |
| `com.july.networking` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.networking#v0.3.0` |
| `com.july.persistence` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.persistence#v0.3.0` |
| `com.july.platform` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.platform#v0.3.0` |
| `com.july.pooling` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.pooling#v0.3.0` |
| `com.july.reddot` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.reddot#v0.3.0` |
| `com.july.resource` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource#v0.3.0` |
| `com.july.resource.yooasset` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource.yooasset#v0.3.0` |
| `com.july.scene` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.scene#v0.3.0` |
| `com.july.tasks` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.tasks#v0.3.0` |
| `com.july.time` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.time#v0.3.0` |
| `com.july.ui` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.ui#v0.3.0` |
| `com.july.ui.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.ui.authoring#v0.3.0` |

## Git dependency limitation

UPM package dependencies declare package names and semantic versions; they cannot infer another package's `?path=` inside this Git monorepo. A Git-based consumer must therefore list the selected package and its July dependency closure explicitly. The seed template demonstrates a complete pinned manifest. A scoped registry can resolve these transitive dependencies automatically when one is introduced later.

Package tests are not part of a production installation. The seed template's validation script temporarily injects the full package set and its `testables` list only while verification runs.
