# Installing July packages

July packages are stored under `Packages/` in one Git repository. A Git dependency selects both a package directory and an immutable repository revision:

```text
https://github.com/XjVoilin/JulyFramework.git?path=/Packages/<package-id>#<package-id>@<version>
```

- `?path=` selects one UPM package from the monorepo.
- `#<package-id>@<version>` pins that package's immutable release tag.
- The tag version must match the selected package's `package.json` version.
- GitHub directory URLs such as `/tree/<tag>/Packages/...` are browser URLs and cannot be passed directly to UPM.

Paste a complete URL into Unity Package Manager through **Add package from git URL**, or add it to `Packages/manifest.json`.

## Copyable package URLs

| Package | Git URL |
| --- | --- |
| `com.july.activity` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.activity#com.july.activity@0.1.0` |
| `com.july.analytics` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics#com.july.analytics@0.3.0` |
| `com.july.analytics.thinkingdata` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics.thinkingdata#com.july.analytics.thinkingdata@0.1.0` |
| `com.july.animation` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.animation#com.july.animation@0.2.0` |
| `com.july.animation.spine` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.animation.spine#com.july.animation.spine@0.1.1` |
| `com.july.arch` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.arch#com.july.arch@0.1.0` |
| `com.july.audio` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.audio#com.july.audio@0.1.0` |
| `com.july.build` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.build#com.july.build@0.3.0` |
| `com.july.build.hybridclr` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.build.hybridclr#com.july.build.hybridclr@0.1.0` |
| `com.july.config` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.config#com.july.config@0.2.0` |
| `com.july.diagnostics` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.diagnostics#com.july.diagnostics@0.1.0` |
| `com.july.events` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.events#com.july.events@0.1.0` |
| `com.july.experiments` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.experiments#com.july.experiments@0.1.0` |
| `com.july.fsm` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.fsm#com.july.fsm@0.1.0` |
| `com.july.guide` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.guide#com.july.guide@0.1.0` |
| `com.july.input` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.input#com.july.input@0.1.0` |
| `com.july.launch` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.launch#com.july.launch@0.1.1` |
| `com.july.localization` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.localization#com.july.localization@0.1.0` |
| `com.july.logging` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.logging#com.july.logging@0.1.0` |
| `com.july.networking` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.networking#com.july.networking@0.3.0` |
| `com.july.networking.protobuf` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.networking.protobuf#com.july.networking.protobuf@0.1.0` |
| `com.july.persistence` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.persistence#com.july.persistence@0.3.0` |
| `com.july.platform` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.platform#com.july.platform@0.3.1` |
| `com.july.pooling` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.pooling#com.july.pooling@0.1.1` |
| `com.july.reddot` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.reddot#com.july.reddot@0.1.1` |
| `com.july.resource` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource#com.july.resource@0.1.0` |
| `com.july.resource.yooasset` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource.yooasset#com.july.resource.yooasset@0.2.3` |
| `com.july.scene` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.scene#com.july.scene@0.1.0` |
| `com.july.tasks` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.tasks#com.july.tasks@0.1.0` |
| `com.july.time` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.time#com.july.time@0.1.0` |
| `com.july.ui` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.ui#com.july.ui@0.2.0` |
| `com.july.ui.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.ui.authoring#com.july.ui.authoring@0.1.0` |

## Git dependency limitation

UPM package dependencies declare package names and semantic versions; they cannot infer another package's `?path=` inside this Git monorepo. A Git-based consumer must therefore list the selected package and its July dependency closure explicitly. The seed template demonstrates a complete pinned manifest. A scoped registry can resolve these transitive dependencies automatically when one is introduced later.

Package tests are not part of a production installation. The seed template's validation script temporarily injects the full package set and its `testables` list only while verification runs.
