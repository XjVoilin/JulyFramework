# July Release

标准项目的完整构建、热更、发布工具和客户端版本协议。与 `com.july.build` 同在 JulyFramework 仓库，不创建新的仓库。

## 包边界

- `com.july.build`：通用步骤契约、执行器、Unity 构建宿主、AOT 源码哈希算法。
- `com.july.build.hybridclr`：依赖 HybridCLR SDK 的编译、备份、元数据检查实现。
- `com.july.release`：标准发布策略与编排；依赖以上能力及 YooAsset、July.Config、持久化和日志能力。
- 项目：现有 BootConfig/BuildConfig 资产、项目目录/字体/启动图/程序集清单，由 Inspector 填写。项目内不再需要构建代码或装配入口。

```text
com.july.release/
  Runtime/                 启动配置契约、共享资源配置、后台版本协议、资源 URL
  Editor/                  BuildConfig 配置资产类型、自动装配、上下文、CI、面板、工具
    Panels/                平台、版本、构建、差异、HybridCLR 面板
    Steps/                 AB、HybridCLR、AOT 归档、COS、Git、预下载
    Platforms/WeChat/      微信 SDK 构建适配
    Platforms/TikTok/      抖音 SDK 构建适配
  Tests/Editor/            路径、版本契约、JS 预请求和编排回归测试
```

SDK 适配使用独立的 Editor asmdef，**没有拆成额外 UPM 包**。公共 `July.Release.Editor` 不引用微信/抖音 SDK。
微信适配引用项目安装的 `WxEditor`、`Wx` 及自动引用的 SDK DLL，受 `JULYGF_WX_MINIGAME` 约束；抖音适配引用 `com.bytedance.ttsdk-editor` 及其 DLL，受 `JULYGF_DY_MINIGAME` 约束。
本轮不搬运 SDK，不通过反射访问 SDK。项目保留当前安装方式；未启用目标 SDK 的平台构建明确失败。

## 接入

1. 项目 AOT asmdef 引用 `July.Release.Runtime`；现有 BootConfig 实现 `IReleaseBootConfig`，并序列化一个 `ReleaseResourceSettings` 字段。它保存包名、下载标签、补充 AOT 程序集清单，构建和运行时从同一份数据读取。
2. 通过 Create → JulyGF → Build Config 创建框架 `BuildConfig` 资产，项目 Assets 下保留唯一一份。在 Inspector 的 Boot Config 字段拖入项目启动配置资产。
3. 在 BuildConfig Inspector 填写 COS 根地址、版本、路径、宏、分组、预下载限制，拖入启动字体和图片。路径相对 Unity 项目根；AOT Archive Parent 自动追加当前项目文件夹名。COS CLI 路径不含扩展名，Windows 自动追加 `.exe`。
4. 原菜单 JulyGF → 构建 → 构建工具由框架直接提供；窗口顶部“构建配置”“启动配置”按钮打开对应 Inspector。构建时根据资产内容生成内存中的 `ReleaseProjectProfile`，不另外保存 CDN/COS 根、项目名或重复资源清单。
5. 配置首次使用时由框架查找；没有配置、存在多份配置或启动配置类型错误时明确报错。SDK 适配程序集通过 `InitializeOnLoadMethod` 自行注册。项目 Editor asmdef 不需要引用 release 或平台适配器，也不需要 `ProjectBuildBinding`。
6. CI 直接使用 `July.Release.Editor.BuildPipelineCI` 的 `FullBuild`、`HotUpdateBuild`、`RunStep`、`SyncPlatformDefines`。已有项目应同步替换 Jenkins/shared-library 的 `-executeMethod` 完整方法名；其他参数保持原样。仍需在单独一轮 Unity 中同步平台宏，编译完成后再调用构建入口。

已有项目迁移 BuildConfig 脚本时保留原脚本 `.meta` GUID，保留配置资产身份及已有字段值。项目运行时 BootConfig 可以继续保留自己的环境枚举、统计配置等内容，通过接口映射即可。热更业务程序集加载顺序仍属于项目启动代码。
改变包名或 DLL 收集路径后，应同步 YooAsset 收集器；HybridCLR 收集器不匹配时构建预检报错，可使用现有初始化 AB 收集器操作更新。静态 Buildin/Lobby 分组标签随共享资源配置同步。

目前提供 WeChat/TikTok 的平台宏、WebGL（团结为 MiniGame）设置及首包预下载策略。未来 Android 接入仍在此包增加原生构建适配、平台设置与相应配方；AB、版本、AOT、COS 和 CI 机制复用。本版本未宣称已经支持 Android，也不需要为 Android 再拆包。

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

## AOT 与升级

AOT 哈希只检查项目指定的 ScriptsAot 目录和平台编译宏，继续排除生成的 HybridCLR 源码目录。**不扫描 UPM 包、不增加包指纹**。UPM 更新后由项目维护者主动执行 FullBuild。
本次迁移新增 AOT 程序集，并改变项目 AOT 源码，首次接入必须 FullBuild，不可直接向旧主包发布热更。

## 临时状态与验证

字体替换在 AB/平台构建作用域内恢复进入前的默认字体和 fallback 列表；平台构建失败时也会解除未完成的 SDK 构建回调作用域。Splash 行为由项目策略指定。
EditorPrefs 按项目目录隔离，首次接入会使用新的构建面板偏好；配置资产中的 env/版本不因此改变。

`Tests/Editor/ReleaseContractTests.cs` 可通过 Unity Test Runner 运行（需要 test framework，并将本包加入 manifest 的 testables）。测试不会构建、上传、修改配置资产或打 tag。
真实 SDK 导出、IL2CPP 与在线后台/COS 集成需要在目标 Unity/团结环境做完整构建验收。

本地开发可用 `file:` 引用此包。正式共享到 CI 时，按仓库的独立包版本规则发布新的不可变包版本 tag（本次装配 API 调整建议发布 `com.july.release@0.2.0`），并将项目依赖固定到该 tag；不要让 CI 依赖开发机绝对路径。
