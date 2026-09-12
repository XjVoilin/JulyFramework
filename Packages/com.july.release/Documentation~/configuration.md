# 发布配置（0.2.0）

## 数据归属

| 数据 | 来源 |
| --- | --- |
| 环境、后台地址、项目 CDN 根 | 项目运行配置，IReleaseBootConfig |
| 内容版本、COS 根、公共宏 | BuildConfig |
| 资源包、项目启动下载标签 | IReleaseResourceConfig.Resources；未提供共享契约时来自 BuildConfig.resources |
| 补充 AOT 程序集 | IReleaseResourceConfig.AdditionalAotMetadataAssemblies；未提供共享契约时来自 BuildConfig.aot.AdditionalAotMetadataAssemblies |
| 热更和 AOT 资源标签 | ReleaseResourceConventions 固定为 HotFix / AotMeta，不在项目配置中重复填写 |
| DLL 输出位置 | YooAsset 的 HotFix / AOTMeta 分组唯一 CollectPath，必须在 Assets 子目录且两组不重叠 |
| 泛型引用文件 | HybridCLR Settings.outputAOTGenericReferenceFile |
| AOT 源码检查范围、生成代码排除项 | BuildConfig.aot |
| COS CLI 及配置文件 | 工具随 Release 包 Tools~/coscli 分发；凭证仍为项目 Tools/coscli/.cos.yaml，不入包 |
| 本地 CDN、AOT 工作副本 | 框架内部约定 CDN、HybridCLRData/AOTBackup |

平台包固定在 ../Build/{Platform}/{CoreVersion}；本地资源仍在 CDN/{Env}/{Platform}/{CoreVersion}/{PlanVersion}。本机 AOT 持久归档固定在 ../AOTBackup/{工程文件夹名}/...。显式 AOT 输入/输出路径仍直接使用调用方传入的目录，不追加层级。不移动或删除已有备份。

## 项目策略

资源分组由项目维护，Release 的本机及 CI 构建均消费已保存的 YooAsset 收集配置，不自动同步分组。GooseMarket 保留项目 Editor 的“JulyGF/资源管理/同步 AB 分组（Lobby + 小游戏）”菜单；修改小游戏目录后使用菜单同步，检查并提交收集配置。MableSorting 无需此工具或配置。通用构建校验不负责识别未加入收集配置的新小游戏。

sharedBundles.enabled 控制自定义目录合包；关闭时使用 YooAsset 标准 TaskGetBuildMap_SBP。fontDirectory 留空时不应用字体专用深度。

launchFont 留空保持 TMP 默认字体及 fallback；设置资产才会在构建作用域内替换并恢复。

HybridCLR 编译拷贝完成后统一生成 hotUpdateDllDirectory/hybridclr-manifest.json。formatVersion 为 1；hotUpdateAssemblies 根据本次实际 DLL 的引用关系按依赖顺序排列，aotMetadataAssemblies 按名称排列。名称不含 .dll 后缀，内容仅记录本次产物，不扫描遗留文件。Bootstrap Player 读取该清单加载热更 DLL，项目只在 HybridCLR Settings 配置热更名单。MableSorting 的旧运行时也读取同名文件，但尚未切换新 Bootstrap。

## 面板与校验

主窗口只有配置、平台、全量/热更、结果。配置编辑直接写原资产；未配置完整时仍能打开配置编辑。检查集中报告本次能力需要的字段，不执行远端版本请求或自动修正资源规则。

删除重复手动编译、Generate All、AOT Hash、初始化分组以及清理本地版本按钮。标准流水线保留自动校验；SDK 自身工具仍可独立使用。

## 开发阶段切换

直接更新到新结构，不保留旧 Paths 数据模型或自动迁移适配。现有两个项目已按各自配置值整理。MableSorting 删除旧 Build 代码、JulyBuildSettings.json 和独立 BootConfig，保留 GameConfig 原 GUID 与有效值；不沿用旧 Build 的生成参数或入口。

版本、FORCE_REBUILD、显式 AOT 输入/输出参数、无参数本机行为保持原契约。AOT 源码已变化且包更新后需要新的全量构建，不能用本次编辑去热更旧主包。

当前项目通过 file: 指向本机框架源码，供开发验证。发布不可变包 tag 后再切换项目引用；未发布前不能把该本机路径当作 Jenkins 可用依赖。

## 框架约定与项目差异

GooseMarket 和 MableSorting 已统一：平台包根目录、本机 AOT 归档父目录、COS 工具/凭证位置、DLL 收集分组名称 HotFix / AOTMeta。上述项目字段已删除，不提供旧字段迁移或兼容。

MableSorting 只把原 Hotfix / AOTMetadata 分组重命名；CollectPath、Startup 标签、GUID 和运行时加载行为不变。GooseMarket 原分组名已符合约定。程序集名称、资源标签、公共宏、小游戏分组同步、合包、字体和预下载策略属于项目语义，不能因两个项目恰好某个数值相同就强制统一。

项目必须提供：运行配置（环境/CDN/后台）、COS 项目根、内容版本、有效收集配置和 AOT 源码范围、实际需要的宏与策略、平台 SDK 及平台身份配置。SDK 生成路径读取现有设置；项目无需再重复配置框架工作目录。

## Unity 与 Jenkins 接口

独立调用 SyncPlatformDefines 准备完整平台设置（目标、完整宏集合、IL2CPP、图形 API），随后新进程调用 FullBuild 或 HotUpdateBuild。窗口复用相同准备实现。Jenkins 不再解析 ProjectSettings YAML 决定是否同步。

CDN 上传必须传 -uploadCdn；未传时只执行本地构建。有凭证文件不再隐式触发上传。-miniGame 继续控制全量是否导出平台包。平台导出目录由框架在出包前清理，只清理本次平台/核心版本，拒绝链接目录。导出失败不会先执行本次 Bundle 上传；整个 CDN 发布仍不是原子事务。

每次 CI 调用开始删除旧 release-build-result.json，执行器结束后写入新结果。schemaVersion=1；succeeded、buildType、platform、buildTarget、environment、coreVersion、planVersion、debug、cdnUploaded、packageVersion、packageDirectory、cdnDirectory、cdnUrl、aotBackupPath、failedStep、error、elapsedSeconds。packageDirectory 来自实际导出的 game.js 所在目录。解析/进程异常可能没有结果文件；不能将旧结果、目录存在或 AOT 成功日志当作成功。

成功必须同时满足 Unity 退出码为 0、结果文件有效且 succeeded=true、身份字段与请求一致、要求上传时 cdnUploaded=true。Jenkins 随后还有平台上传和 Source 发布；Unity 的成功不代表这两者成功。Git 标签完全由 Jenkins 管理，Unity 不读取 BUILD_NUMBER，也不创建或推送发布标签。

本轮两端接口必须一起更新。项目仍使用开发机 file: 包引用；发布并固定新的不可变包 tag 后再部署到打包机，不移动旧 tag。

## 内置 COSCLI

com.july.release 内置现有 COSCLI v1.0.8（Windows x64、macOS ARM64），SHA-256 与腾讯云官方同版本发布一致。项目不再下载/复制 coscli，可执行路径由 PackageInfo.FindForAssembly 的 resolvedPath 定位，适用于本地 file:、Git 和缓存安装。工具不从项目目录或 PATH 兜底，也不在构建时下载。

凭证仍由本机或 Jenkins 提供到 Tools/coscli/.cos.yaml，Jenkins 注入流程无需更改。macOS 在 Library/July.Release/Tools 中创建可执行工作副本；包目录和 PackageCache 保持只读。其他编辑器主机平台/架构暂未提供，上传时明确报错，本地不上传的构建无需此工具。

工具版本、来源、许可证见 Tools~/coscli/README.md 与 THIRD-PARTY-NOTICES.md。GooseMarket 原二进制已移入包；原凭证和日志未迁移或修改。遗留 Tools/upload_cdn.sh 不属于当前构建流程。

Launch 由 Unity 构建场景列表随主包携带，不再放入 YooAsset 收集分组。Release 不再维护 BuiltInTag，也不按 Buildin 标签跳过预下载；YooAsset 的实际内置文件复制设置继续由其自身构建设置决定。
