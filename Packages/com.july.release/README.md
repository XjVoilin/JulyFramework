# July Release

标准项目的完整构建、热更、发布工具和客户端版本协议。与 `com.july.build` 同在 JulyFramework 仓库，不创建新的仓库。

## 包边界

- `com.july.build`：通用步骤契约、执行器、Unity 构建宿主、AOT 源码哈希算法。
- `com.july.build.hybridclr`：依赖 HybridCLR SDK 的编译、备份、元数据检查实现。
- `com.july.release`：标准发布策略与编排；依赖以上能力及 YooAsset、July.Config、持久化和日志能力。
- 项目：现有 BootConfig/BuildConfig 资产、项目目录/字体/启动图/程序集清单，以及装配入口。

```text
com.july.release/
  Runtime/                 后台版本契约、运行时配置快照、资源 URL
  Editor/                  上下文、CI、面板容器、项目装配契约、工具
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

1. 项目 Editor 装配入口调用 `ReleaseProject.Configure`，提供 `ReleaseProjectProfile`、读取现有配置资产的两个适配器，以及平台构建器工厂。使用 `[InitializeOnLoad]` 初始化；旧 CI/菜单转发器在调用前明确触发初始化。
2. 项目 Editor asmdef 引用 `July.Release.Editor`、`July.Release.Runtime` 及两个平台 Editor asmdef；项目 AOT asmdef 引用 `July.Release.Runtime`。SDK 宏控制相应平台代码。
3. `ReleaseProjectProfile` 是代码中的项目资产绑定，不是新增 ScriptableObject。它不保存 CDN/COS 根地址，也没有项目名字段。
4. 项目可保留原 ConfigSnapshot 服务类型，以组合方式委托 `ReleaseConfigSnapshot`，保持 July 服务注册和现有调用方不变。
5. 旧 `-executeMethod` 类保留薄转发；无需修改 Jenkins 参数或 shared-library。

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

FullBuild 以 PlanVersion 创建 CoreVersion 并设置主包版本；HotUpdate 使用所选 AOT 备份的 CoreVersion，PlanVersion 可独立递增。YooAsset PackageVersion 继续使用原时间戳语义，Git tag 规则保持原样。

## AOT 与升级

AOT 哈希只检查项目指定的 ScriptsAot 目录和平台编译宏，继续排除生成的 HybridCLR 源码目录。**不扫描 UPM 包、不增加包指纹**。UPM 更新后由项目维护者主动执行 FullBuild。
本次迁移新增 AOT 程序集，并改变项目 AOT 源码，首次接入必须 FullBuild，不可直接向旧主包发布热更。

## 临时状态与验证

字体替换在 AB/平台构建作用域内恢复进入前的默认字体和 fallback 列表；平台构建失败时也会解除未完成的 SDK 构建回调作用域。Splash 行为由项目策略指定。
EditorPrefs 按项目目录隔离，首次接入会使用新的构建面板偏好；配置资产中的 env/版本不因此改变。

`Tests/Editor/ReleaseContractTests.cs` 可通过 Unity Test Runner 运行（需要 test framework，并将本包加入 manifest 的 testables）。测试不会构建、上传、修改配置资产或打 tag。
真实 SDK 导出、IL2CPP 与在线后台/COS 集成需要在目标 Unity/团结环境做完整构建验收。

本地开发可用 `file:` 引用此包。正式共享到 CI 时，按仓库的独立包版本规则发布不可变 `com.july.release@0.1.0` tag，并将项目依赖固定到该 tag；不要让 CI 依赖开发机绝对路径。
