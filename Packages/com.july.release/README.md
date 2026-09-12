# July Release

标准项目的完整构建、热更、发布工具和客户端版本协议。与 `com.july.build` 同在 JulyFramework 仓库，不创建新的仓库。

## 包边界

- `com.july.build`：通用步骤契约、执行器、Unity 构建宿主、AOT 源码哈希算法。
- `com.july.build.hybridclr`：依赖 HybridCLR SDK 的编译、备份、元数据检查实现。
- `com.july.release`：标准发布策略与编排；依赖以上能力及 YooAsset、July.Config、持久化和日志能力。
- 项目：提供运行配置与唯一 BuildConfig，引用既有收集配置（DLL 组统一 HotFix / AOTMeta）。框架管理目录约定；项目策略明确启用。

```text
com.july.release/
  Runtime/                 启动配置契约、共享资源配置、后台版本协议、资源 URL
  Editor/                  BuildConfig 配置资产类型、自动装配、上下文、CI、面板、工具
    Panels/                平台、版本、构建与结果差异；配置和接入检查集中在构建窗口
    Steps/                 AB、HybridCLR、AOT 归档、COS、预下载
    Platforms/WeChat/      微信 SDK 构建适配
    Platforms/TikTok/      抖音 SDK 构建适配
  Tests/Editor/            路径、版本契约、JS 预请求和编排回归测试
```

SDK 适配使用独立的 Editor asmdef，**没有拆成额外 UPM 包**。公共 `July.Release.Editor` 不引用微信/抖音 SDK。
微信适配引用项目安装的 `WxEditor`、`Wx` 及自动引用的 SDK DLL，受 `JULYGF_WX_MINIGAME` 约束；抖音适配引用 `com.bytedance.ttsdk-editor` 及其 DLL，受 `JULYGF_DY_MINIGAME` 约束。
本轮不搬运 SDK，不通过反射访问 SDK。项目保留当前安装方式；未启用目标 SDK 的平台构建明确失败。

## 接入

热更程序集的唯一配置入口是 HybridCLR Settings。Release 根据本次复制 DLL 的真实依赖生成 formatVersion 为 1 的 hybridclr-manifest.json，Bootstrap 下载后读取，不在 GameConfig 重复保存名单。旧清单需要通过当前构建流程重新生成。

1. 项目运行配置实现 `IReleaseBootConfig`，提供环境、CDN 和后台地址；无需命名为 BootConfig。MableSorting 已将启动配置合并到 GameConfig。
2. 创建唯一 BuildConfig，引用运行配置，填写 COS 项目根、版本和公共宏；平台产物固定输出到 ../Build。
3. 引用已有 YooAsset 收集资产，DLL 分组统一命名为 HotFix / AOTMeta，资源标签分别为框架固定的 HotFix / AotMeta。DLL 输出目录读取分组唯一 CollectPath；泛型引用路径读取 HybridCLR Settings。不会自动重写现有资源分组。
4. 项目若已经在运行时使用 `ReleaseResourceSettings`，可实现独立的 `IReleaseResourceConfig`，共享资源设置实例和热更配置中的 AdditionalAotMetadataAssemblies；否则资源参数由 BuildConfig.resources、补充 AOT 清单由 BuildConfig.aot.AdditionalAotMetadataAssemblies 保存，不要求运行配置承担构建策略。
5. 在 JulyGF → 构建 → 构建工具中编辑原配置、选择平台与构建类型，执行“检查本次构建接入”。检查不联网、不构建、不上传、不自动修改收集器；真正构建仍执行 AOT、版本和上传校验。
6. 小游戏分组同步、目录共享包合并默认关闭，启动字体留空表示不替换。没有“维护与诊断”面板；编译和 AOT Hash 检查由标准流程负责，产物目录在结果区打开。
7. 平台 SDK 继续由项目安装。CDN 和出包路径由框架同步；项目 AppID 等在 SDK 自身配置中维护。
8. CI 继续使用 `July.Release.Editor.BuildPipelineCI.FullBuild` / `HotUpdateBuild` / `RunStep` / `SyncPlatformDefines`；版本语义保持，上传须显式传 -uploadCdn，完整接口见发布配置文档。

具体字段归属见 [发布配置](Documentation~/configuration.md)。0.2.0 直接采用新结构，不提供旧 Paths、旧入口或旧配置格式的兼容层。

## 构建工具面板

- 主界面选择平台、环境、Debug、全量/热更、QA 和上传。默认使用完整构建配方；需要重跑部分步骤时启用“自定义构建步骤”。旧的步骤偏好仅在自定义模式中生效。
- 本次 CoreVersion、PlanVersion、本地目录、导出位置和上传范围由同一份构建选择生成，预览与执行一致。资源版本直接编辑配置资产；外部 Inspector 修改后会同步显示。
- 热更构建与维护区的热更编译共用一个 AOT 基线，按平台/BuildTarget 保存选择。备份在目标变化、构建结束、项目资源变化或点击刷新时更新，不在每次绘制时遍历目录。
- 线上版本查询使用本次预览的 CoreVersion，切换查询环境/平台/CoreVersion 后旧结果失效。QA 面板跳过查询，固定版本允许重复覆盖。
- QA 全量构建只临时设置 PlayerSettings.bundleVersion，并在 finally 中恢复进入构建前的值，包括取消和失败路径；不将 PlanVersion 当作恢复值。
- 上传开关统一控制 AB、微信 data、preload 的远端写入。关闭上传仍可出包、在有本次 AB 产物时生成本地 preload，并注入 game.js；该 CDN 模式产物运行前仍需通过其他流程将资源放到对应 CDN。共享 FullBuild 配方的 upload=false（包括 CI）同样不上传 data/preload。
- 结果区显示最近构建的状态、耗时、实际版本和差异，并提供实际产物目录入口。取消/未执行 AB 的构建不显示之前的差异。
- 维护与诊断默认折叠，保留 HybridCLR 单步、收集器初始化、配置检查、Hash 和本地清理。清理仅针对预览中的 Env/Platform/CoreVersion/PlanVersion，显示确切路径并保留删除确认；不清理其他版本或远端资源。

## 资源和版本契约

- BootConfig.cdnUrl：**完整项目 CDN 根 URL**，允许路径前缀。
- BuildConfig.cloudUrl：**完整项目 COS 根 URL**，允许相同路径前缀。
- `ParseCloudInfo` 返回 bucket/region/pathPrefix；`BuildCosObjectUrl` 统一拼接对象路径。
- 前缀区分大小写，忽略首尾斜杠；CDN/COS 前缀不一致时，在上传前报错。
- 公共 bundle URL：`{cdn}/{Env}/{Platform}/{CoreVersion}/{PlanVersion}/{bundle}`。
- preload 和微信 data：`{cdn}/{Env}/{Platform}/{CoreVersion}/{file}`。
- 微信 ProjectConf.CDN 使用完整项目根；dataFileSubPrefix 仅含 `{Env}/{Platform}/{CoreVersion}/`。
- 本地保持 `CDN/{Env}/{Platform}/{CoreVersion}/{PlanVersion}`；COS 同步不使用删除选项，不清理远端旧版本。

`POST /client_version` 的请求为 `{"CoreVersion":"..."}`，响应读取 `platforms.{platform}.PlanVersion`。运行时、编辑器版本查询、game.js 配置预请求共用这一契约，预请求使用 `July.Config` 的现有 JS 缓存桥接。

FullBuild 以 PlanVersion 创建 CoreVersion 并设置主包版本；HotUpdate（CI 与面板）使用所选 AOT 备份的 CoreVersion，PlanVersion 可独立递增。单步入口 RunStep 优先采用已有参数 -aotBackupVersion，未指定时使用 PlayerSettings.bundleVersion，不修改主包版本。上传前的线上版本查询传入本次构建的 CoreVersion；独立版本查询按钮使用当前项目的主包版本。缺失 CoreVersion 的上下文在执行步骤前报错。YooAsset PackageVersion 继续使用原时间戳语义，Git tag 规则保持原样。

## 显式 AOT 备份目录

从 0.1.4 起，FullBuild 可传 `-aotBackupOutputPath`，HotUpdateBuild 可传 `-aotBackupInputPath`。目录直接指向备份本身，框架负责保存、完整性校验和工作副本恢复。热更清单中的版本必须符合同时传入的 `-aotBackupVersion` 断言，平台和 BuildTarget 必须符合本次请求。显式路径失败不会使用本地备份兜底；不传路径时保留原有开发机行为。详见 [AOT 备份接口](Documentation~/aot-backups.md)。

## CI 强制重建

`-forceRebuild` 是无值开关，适用于 FullBuild / HotUpdateBuild（以及 RunStep）和 Dev / Test / Prod 各环境。未传入时 `BuildContext.ForceRebuild` 为 false，不保存为面板偏好或配置资产。

Jenkins `FORCE_REBUILD` 应每次默认关闭，仅本次勾选时给 Unity 添加 `-forceRebuild`。参数说明建议为：“强制重新构建并允许覆盖同版本或较低版本资源；支持全量、热更及所有环境，仅对本次构建生效，默认关闭。”shared-library 由打包机维护。

强制模式跳过上传前的线上 PlanVersion 冲突检查，并记录环境、平台、CoreVersion、PlanVersion 和“强制重建，允许覆盖”。COS 配置/路径校验、AOT 检查和上传错误处理仍然执行。

Git 标签由 Jenkins 创建。主标签存在时使用带构建号的归档标签；补发确认已有远端标签指向原提交则成功，否则拒绝覆盖。强制重建不允许移动或强推历史标签。

## AOT 与升级

AOT 哈希只检查项目指定的 ScriptsAot 目录和平台编译宏，继续排除生成的 HybridCLR 源码目录。**不扫描 UPM 包、不增加包指纹**。UPM 更新后由项目维护者主动执行 FullBuild。
本次迁移新增 AOT 程序集，并改变项目 AOT 源码，首次接入必须 FullBuild，不可直接向旧主包发布热更。

## 临时状态与验证

字体替换在 AB/平台构建作用域内恢复进入前的默认字体和 fallback 列表；平台构建失败时也会解除未完成的 SDK 构建回调作用域。Splash 行为由项目策略指定。
EditorPrefs 按项目目录隔离，首次接入会使用新的构建面板偏好；配置资产中的 env/版本不因此改变。

`Tests/Editor/ReleaseContractTests.cs` 可通过 Unity Test Runner 运行（需要 test framework，并将本包加入 manifest 的 testables）。测试不会构建、上传、修改配置资产或打 tag。
真实 SDK 导出、IL2CPP 与在线后台/COS 集成需要在目标 Unity/团结环境做完整构建验收。

本地开发可用 `file:` 引用此包。正式共享到 CI 时，按仓库的独立包版本规则发布新的不可变包版本 tag（本次装配 API 调整建议发布 `com.july.release@0.2.0`），并将项目依赖固定到该 tag；不要让 CI 依赖开发机绝对路径。

CI 平台准备、显式 -uploadCdn、release-build-result.json 及项目/框架职责见 [发布配置](Documentation~/configuration.md)。发布标签由 Jenkins 维护。

COSCLI v1.0.8 随本包 Tools~/coscli 分发（Windows x64/macOS ARM64），项目只提供 Tools/coscli/.cos.yaml 凭证。来源和许可见 THIRD-PARTY-NOTICES.md。
