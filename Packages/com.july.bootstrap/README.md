# July Bootstrap

可选的 July.Launch 标准启动实现。包维护具体 Step 和固定顺序，项目提供配置与画面。使用当前标准框架组合的项目更新本包及声明的模块版本即可取得共同改进。

```csharp
Bootstrap.Configure(pipeline, gameConfig.Bootstrap, launchView,
    AOTGenericReferences.PatchedAOTAssemblyList);
```

最后一个输入来自项目构建生成代码，不复制到 Inspector。Configure 只接受新 Pipeline，完成后封闭计划；不提供任意步骤插入协议，特殊流程直接使用 Launch。

## 配置与职责

BootstrapConfig 组合 ReleaseSettings、ThinkingDataConfig、PlatformConfig、ResourceStartupConfig、HotUpdateConfig 和日志通道。Resource 直接显示 PackageName、PlayMode、StartupDownloadTags，不再包含 Content。HotUpdate 只保存 AdditionalAotMetadataAssemblies 和 Registrar 定位信息；热更程序集仅在 HybridCLR Settings 配置。具体业务注册仍由项目 HotUpdateRegistrar 实现。

ResourceStartupConfig 继承 ReleaseResourceSettings 的资源分发字段，仅增加 YooAsset 运行模式；Release 无需依赖 YooAsset 运行时或 Bootstrap。GameConfig 通过 IReleaseResourceConfig 返回同一个 Resource 实例，以及 HotUpdate 中的补充 AOT 清单，构建端不复制配置。

各 Step 只接收本阶段需要的配置；平台、资源等运行时模块从 Arch 获取。AppRegistration 仅保存被后续步骤调用的同一个 Registrar。没有通用 Resolve 容器、Profile、Session 或 BootstrapOptions 映射。

LaunchInfoStore 只保存成功发布的只读运行信息；首次成功前读取明确失败，后续成功重试整体替换。Store 不持有 GameConfig、可变 ReleaseConfigSnapshot、视图或模块引用，也不执行网络请求。

## 资源与热更配置怎么填写

```text
Bootstrap
├─ Resource
│  ├─ PackageName                YooAsset 包名称
│  ├─ PlayMode                   资源运行模式
│  └─ StartupDownloadTags        进入游戏前必须下载的项目资源
└─ HotUpdate
   ├─ AdditionalAotMetadataAssemblies    额外需要补充元数据的 AOT 程序集
   ├─ RegistrarAssembly          注册入口所在程序集，不含 .dll
   └─ RegistrarType              注册入口完整类型名
```

- 新增启动必需的公共 UI 或配置：在 YooAsset 收集器中设置项目标签，再加入 StartupDownloadTags。通常无需新增字段或 Step。
- 进入玩法后才需要的内容：由玩法按需加载，不加入启动下载清单，避免拖慢进入速度。
- 热更 DLL 使用 HotFix 标签，AOT 元数据使用 AotMeta 标签，由 ReleaseResourceConventions 统一定义。收集分组名称仍为 HotFix / AOTMeta，注意 AOTMeta 分组与 AotMeta 标签的大小写不同。构建接入检查会报告缺失的代码标签。
- RequiredDownloadTags 自动将两个代码标签与 StartupDownloadTags 合并去重，构建预下载和运行时下载共用该结果。标签不决定程序集加载顺序，顺序由 Release 读取实际 DLL 依赖后写入清单。
- 下载表示文件可用，不表示已加载到内存。Launch 由 Unity 构建场景列表随主包携带，不需要再收集为 YooAsset 资源。
- 新增热更程序集：只更新 HybridCLR Settings，随后通过 Release 构建。构建按实际 DLL 依赖生成加载顺序，无需再维护 GameConfig 名单。AOT 自动清单仍使用生成的 PatchedAOTAssemblyList，只有分析遗漏项才填写 AdditionalAotMetadataAssemblies。

GooseMarket 保留 DefaultPackage、Lobby 和补充 AOT 清单。Game.Runtime、MiniGames 的加载顺序由构建读取 DLL 依赖决定。MableSorting 尚未迁移；未来使用此 Bootstrap 时，可继续使用 Startup 表达业务启动资源，同时需要为 DLL 收集器补齐 HotFix / AotMeta 约定标签。

## 顺序与扩展

首帧 → 基础模块 → 平台登录/发布信息并行 → 资源初始化 → 标签下载 → AOT/热更装载 → Registrar 注册 → 预初始化/系统初始化 → 游戏入口 → 平台延迟初始化。

新增共同工作：实现一个 ILaunchStep，在 Bootstrap.Configure 的列表中加入它，并在同一行按实际需要声明重试和统计 ID。内部统一连接进度、失败交互和成功统计，不要求各项目补回调。不可恢复异常由整个 Pipeline 的失败观察通知视图，取消不作为故障显示。

并行取消与收尾、可恢复 false 的重试属于 Launch。程序集装载、注册和系统初始化不整步重试。现有登录、资源初始化和 Registrar.OnGameLaunch 等底层操作不能完全被 token 中断时，等待真实结束再结束本次启动，不伪装回滚。

## 项目视图与入口

项目视图实现 IBootstrapView：SetStepInfo、ShowFailureAsync、PrepareForGame、Complete。ShowFailureAsync 返回 Retry 或 Restart，视图只展示允许的操作，Bootstrap 执行平台行为。Complete 由整条 Pipeline 成功触发。项目维护自己的画面层级、按钮、动画及事件退订。

项目 GameEntry 继承 BootstrapGameEntry。该入口创建 Arch 并驱动已初始化 System 的更新，更新不依赖整条 Pipeline 完成。销毁时先取消并等待启动任务收尾，再执行 OnShutdown；项目 override OnShutdown 时清理自己的资源并调用 base。应用退出与一次性启动状态分离，不新增 BootstrapSystem 或 Procedure 包装。

## 验证范围

首个迁移项目是 GooseMarket。相关 Runtime/Editor 源码隔离编译、微信条件程序集编译和 36 项独立 .NET 针对性测试通过；这些不替代 Unity Play Mode 和真机 SDK/HybridCLR 验证。本机未完成抖音 SDK 构建验证。

包声明 WeChat/TikTok 与小游戏文件系统的真实程序集依赖，宿主仍需安装目标平台 SDK。本次改变 AOT/UPM 接口，必须重新完整构建主包；没有发布 tag，不能对旧主包直接应用本轮改造。

## 2026-09-11 启动评审修复

标准流程最后增加 InitializeDeferredServicesStep。平台广告、分享等 DeferredInit 由 Bootstrap 在项目 OnGameLaunch 返回后统一触发，再完成启动画面。Goose 已移除原来的重复调用；项目的字体、业务注册与首个场景选择仍归项目。

IHotUpdateRegistrar.OnGameLaunch 接收 CancellationToken。Goose 将它传至账号登录、字体、大厅和小游戏入口；账号重试循环使用关联令牌，取消直接结束，不等 Arch.Shutdown 才取消。小游戏入口对取消单独传播，不把取消当作失败后返回大厅。仍无取消能力的小游戏业务钩子等待其结束后检查令牌，不用丢弃后台任务的方式伪装取消。

Arch 在初始化期间向 SystemBase.InitializationToken 传递令牌，Platform 使用该令牌配置 SDK。平台登录的取消结束本次等待；原生 SDK 请求没有取消接口，晚到回调仅完成局部结果，不再回写 Code。平台初始化失败会清理已经建立的局部 SDK 状态。

JS 预取响应携带 requestUrl、environment、platform、coreVersion 和 responseJson。Release 在身份完全匹配时采用，否则正常请求；旧格式与坏缓存也走正常请求。预取注入脚本与运行时协议同步修改，需要重新构建主包。

AOT 资源恢复并行读取，全部成功后按清单顺序应用元数据；读取失败、取消或应用失败都会等待已开始的读取收尾并释放全部已取得句柄。热更程序集仍按依赖顺序加载，不重试不可逆的装载。

ReleaseResourceSettings.StartupDownloadTags 表达项目启动内容；RequiredDownloadTags 将其与框架固定的 AOT、热更标签合并去重，构建和运行使用同一结果。Goose 配置 Lobby，Mable 可以使用 Startup，公共配置不再固定要求大厅。PlatformConfig 中的微信/抖音广告位由项目填写，空值明确表示该项目不启用对应激励广告，框架中没有项目广告位常量。

验证：13 个相关 Runtime/Editor/微信平台程序集源码编译、Bootstrap 微信 Player 条件编译、36 项独立 .NET 测试通过；微信和抖音的实际生成预取脚本及 JS 桥接在 Node 中执行通过。未做 Unity Play Mode 或真机冷启动耗时测量，抖音适配器没有完整 SDK 编译条件。

Mable 尚未切换新包。接入仍需迁移 GameConfig/LaunchView、对齐 hybridclr-manifest 的程序集清单、让 HTTP 读取启动结果的业务地址，并重新完整构建；不能把本次框架修复理解为 Mable 已完成迁移。

## 热更程序集清单的唯一来源

HybridCLR Settings → 编译并复制本次 DLL → Release 调用 HybridCLR 的 AssemblySorter 按依赖排序 → 生成 HotFix 分组中的 hybridclr-manifest.json → Bootstrap 在代码下载后读取清单 → 补充 AOT 元数据 → 按清单顺序加载热更 DLL → 执行 Registrar。

GameConfig 不再包含 HotUpdate.Assemblies。RegistrarAssembly 仍由项目指定，但 Player 在装载任何程序集前验证它包含在构建清单中。清单文本读取后释放，AOT 并行读取、热更 DLL 顺序加载、业务入口和平台延迟初始化的顺序保持不变。

清单 formatVersion 为 1，表示 hotUpdateAssemblies 已按 DLL 依赖排序。旧清单没有这个标记，启动会明确提示重新构建；不会回退到旧资产中的名单或扫描目录猜测。AOT 启动清单仍来自 PatchedAOTAssemblyList 加 AdditionalAotMetadataAssemblies，本次不改变 AOT 数据来源。

Editor 继续跳过动态程序集加载，不要求每次 Play 前先生成清单；Player 必须使用本次构建生成的清单及匹配的 DLL。现有产物目录和旧清单未手动修改，需重新完整构建主包与资源后验证真机。

## Launch 与资源收集的边界

Launch 保留在 Unity 构建场景列表中，随主包启动。YooAsset 不再收集 Launch，项目同步菜单只维护 Lobby 和小游戏分组。GameConfig.Resource 只包含 PackageName、PlayMode、StartupDownloadTags。

公共配置移除 BuiltInTag，Release 预下载筛选不再仅凭 Buildin 标签推断资源已内置并跳过文件。启动必需标签、预下载数量和字节上限保持原有规则。YooAsset 自身的内置文件复制设置保持原状，本次不新增分发策略。

源码和收集配置已修改，现有构建产物需重新构建后生效；未手工清理缓存或 CDN 文件。
