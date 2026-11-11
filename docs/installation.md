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

## URP mesh highlighting

`com.july.rendering.urp` provides optional orthographic URP static-mesh highlighting. Select the released package using the immutable URL in the table below. Add `com.july.rendering.urp` to a consuming project's `testables` to run its native lifecycle tests. Its runtime assembly belongs in the player; do not list it as a HybridCLR hot-update assembly.

## Copyable package URLs

| Package | Git URL |
| --- | --- |
| `com.july.activity` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.activity#com.july.activity@0.1.1` |
| `com.july.analytics` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics#com.july.analytics@0.3.1` |
| `com.july.analytics.tencentdn` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics.tencentdn#com.july.analytics.tencentdn@0.1.0` |
| `com.july.analytics.thinkingdata` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.analytics.thinkingdata#com.july.analytics.thinkingdata@0.1.2` |
| `com.july.animation` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.animation#com.july.animation@0.2.7` |
| `com.july.animation.spine` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.animation.spine#com.july.animation.spine@0.1.1` |
| `com.july.arch` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.arch#com.july.arch@0.1.3` |
| `com.july.assetcheck` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.assetcheck#com.july.assetcheck@0.8.1` |
| `com.july.audio` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.audio#com.july.audio@0.2.0` |
| `com.july.bootstrap` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.bootstrap#com.july.bootstrap@0.3.1` |
| `com.july.build` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.build#com.july.build@0.3.0` |
| `com.july.build.hybridclr` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.build.hybridclr#com.july.build.hybridclr@0.1.1` |
| `com.july.config` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.config#com.july.config@0.2.1` |
| `com.july.diagnostics` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.diagnostics#com.july.diagnostics@0.1.3` |
| `com.july.events` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.events#com.july.events@0.1.0` |
| `com.july.experiments` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.experiments#com.july.experiments@0.1.1` |
| `com.july.fsm` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.fsm#com.july.fsm@0.1.0` |
| `com.july.guide` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.guide#com.july.guide@0.4.1` |
| `com.july.input` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.input#com.july.input@0.3.3` |
| `com.july.launch` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.launch#com.july.launch@0.2.1` |
| `com.july.localization` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.localization#com.july.localization@0.1.3` |
| `com.july.logging` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.logging#com.july.logging@0.1.0` |
| `com.july.mesh.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.mesh.authoring#com.july.mesh.authoring@0.2.0` |
| `com.july.networking` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.networking#com.july.networking@0.3.2` |
| `com.july.networking.protobuf` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.networking.protobuf#com.july.networking.protobuf@0.1.0` |
| `com.july.persistence` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.persistence#com.july.persistence@0.3.2` |
| `com.july.physics.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.physics.authoring#com.july.physics.authoring@0.6.0` |
| `com.july.platform` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.platform#com.july.platform@0.5.5` |
| `com.july.pointallocation` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.pointallocation#com.july.pointallocation@0.1.1` |
| `com.july.pooling` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.pooling#com.july.pooling@0.1.1` |
| `com.july.reddot` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.reddot#com.july.reddot@0.3.1` |
| `com.july.release` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.release#com.july.release@0.6.2` |
| `com.july.rendering.urp` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.rendering.urp#com.july.rendering.urp@0.2.7` |
| `com.july.resource` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource#com.july.resource@0.2.0` |
| `com.july.resource.yooasset` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.resource.yooasset#com.july.resource.yooasset@0.3.3` |
| `com.july.scene` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.scene#com.july.scene@0.1.2` |
| `com.july.tasks` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.tasks#com.july.tasks@0.1.2` |
| `com.july.time` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.time#com.july.time@0.1.3` |
| `com.july.ui` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.ui#com.july.ui@0.4.2` |
| `com.july.ui.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.ui.authoring#com.july.ui.authoring@0.2.1` |
| `com.july.config.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.config.authoring#com.july.config.authoring@0.1.0` |
| `com.july.font.authoring` | `https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.font.authoring#com.july.font.authoring@0.1.1` |

## Git dependency limitation

UPM package dependencies declare package names and semantic versions; they cannot infer another package's `?path=` inside this Git monorepo. A Git-based consumer must therefore list the selected package and its July dependency closure explicitly. The seed template demonstrates a complete pinned manifest. A scoped registry can resolve these transitive dependencies automatically when one is introduced later.

Package tests are not part of a production installation. The seed template's validation script temporarily injects the full package set and its `testables` list only while verification runs.

## 资源检查工具

`com.july.assetcheck` 0.8.1 仅在编辑器运行，没有运行时代码；依赖 `com.july.logging` 输出错误与异常。同时依赖 Unity 内置的 Audio 和 JSON 模块；URP 为可选项，不产生 URP 程序集依赖。通过 Package Manager 的 **Add package from git URL** 安装：

```text
https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.assetcheck#com.july.assetcheck@0.8.1
```

通过 Git 安装时，还需显式安装上表中的 `com.july.logging`、`com.july.release` 及其 July 依赖闭包；同时安装包声明的 TextMeshPro 与 Newtonsoft JSON 依赖。

它取代原 Diagnostics 中的小游戏纹理检查窗口。Diagnostics 0.1.3 已移除旧编辑器入口；使用下列独立版本标签更新现有安装，避免重复菜单：

```text
https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.diagnostics#com.july.diagnostics@0.1.3
```

## 碰撞体编辑工具

`com.july.physics.authoring` 0.6.0 为 Editor-only 独立 UPM 包，不依赖外部环境或其它 July 包。整体包围仅输出 Box／Sphere／Capsule，拾取结构贴合原生优先并按预算回退凸包；支持候选暂存、手动直接复用、批量应用／Undo、性能提醒及项目适配总览。使用固定发布标签安装：

```text
https://github.com/XjVoilin/JulyFramework.git?path=/Packages/com.july.physics.authoring#com.july.physics.authoring@0.6.0
```

0.6.0 为 Editor-only。升级前先移除旧 ColliderAuthoringTarget、PhysicalColliderAuthoringTarget、FineSphereEditData 元数据组件；确认无其他用途后再清理旧配方拥有的禁用备份，保留启用碰撞体和 Mesh。同步删除 Runtime／Editor asmdef 与 link.xml 对 `July.Physics.Authoring` 的引用；项目 Editor 适配改为仅引用 `July.Physics.Authoring.Editor`。包不会自动改写使用者资源。

项目专属的碰撞体配置和序列化引用更新保留在项目编辑器程序集。调用方式、算法限制和测试说明见包内 README。各 URL 均绑定不可变的独立包版本标签，不依赖 `main` 分支。

## 配置表导出工具

`com.july.config.authoring` 0.1.0 已按独立包标签发布，使用上表的固定 Git URL 安装。依赖 `com.july.json` 及 Unity IMGUI。项目保留原有 Tools/Luban/Luban 工具，不由本包下载或升级。设置位于 Project Settings → July → 配置表，生成入口为 JulyGF → 配置表 → 生成窗口。扩展与 CI 说明见包内 README。

## 字体裁剪工具

`com.july.font.authoring` 0.1.1 已按独立包标签发布，使用上表的固定 Git URL 安装。仅依赖 Unity IMGUI 与 JSON 内置模块，不依赖其它 July 包、TMP 或本地化。入口为 JulyGF → 资源管理 → 字体裁剪。自动检测可用的 Python 3.10+（含 pip）并显示路径与版本，无需手动指定；窗口可将固定版本 fontTools 安装到当前项目 Library/July/FontAuthoring。输出固定在源字体同级目录，生成成功后自动打开输出目录；使用方式与验证说明见包内 README。
