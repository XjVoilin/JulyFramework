# July Analytics Tencent DN

腾讯 DataNexus IAA 微信小游戏接入包。Runtime 提供配置契约、`IBIEvent` 数据和静态 `TencentDnAnalytics` 发送入口，内部原生声明与 jslib 由本包维护；Editor 使用 Release 的通用扩展复制 SDK、生成配置并处理 game.js。本包不自动注册统计通道。

数数与腾讯由业务分别触发。项目通过 `TencentDnAnalytics.Track(IBIEvent)` 发送腾讯事件；不要把这些事件送入已配置数数通道的 `IAnalyticsSystem`，除非项目明确希望数数也收到它们。项目新增事件同样实现 `IBIEvent`，字段差异保留在 `ToParams()` 中，无需增加桥接函数。

## 项目接入

```csharp
using July.Analytics.TencentDn;

// 启动组合处，在登录和业务上报前配置一次。SDK 已在 game.js 中初始化。
TencentDnAnalytics.Configure(gameConfig.TencentDn);

// 登录取得真实微信 OpenID 后主动调用。
TencentDnAnalytics.SetOpenId(openId);

// 首屏完成处、教程完成处等真实业务位置分别调用。
TencentDnAnalytics.Track(new TencentDnLoadFinishEvent());
TencentDnAnalytics.Track(new TencentDnTutorialFinishEvent());
```

`Configure` 只读取启用开关并检查 JS SDK 就绪状态，不创建 SDK。不启用或非微信 Player 时发送返回 false；启用但 JS 初始化失败会记录错误，并停用本次发送。`Track` 与 `SetOpenId` 返回 SDK 是否接受调用，不代表服务器已经收到。

包不监听登录或玩法事件，不保存加载完成去重状态，不计时或累计关卡数据。项目负责调用时机与次数。`Track` 使用非泛型 `IBIEvent` 参数，允许热更程序集定义自己的事件，不增加 AOT 值类型泛型实例化要求。

运行程序集及原生声明随主包进入 AOT，包内 link.xml 保留可仅由热更代码调用的实现。jslib 只在 `JULYGF_WX_MINIGAME` 下参与微信导出，原生符号使用 `July_TencentDn_` 前缀。首次接入、更新包或改动配置后必须通过 Release 完整构建，不能只向旧主包发布热更。直接使用厂商导出面板会绕过 Release 的导出后处理。

SDK 固定使用 `SDK~/dn-sdk-minigame.js` 文件名。后续升级直接替换此文件，保持 SDK 导出接口兼容，并更新 `THIRD-PARTY-NOTICES.md` 的版本、来源和完整性记录，无需修改 import 或导出处理代码。

## 本次范围

包含事件数据、配置契约、静态发送入口、jslib、导出处理和 SDK 1.5.11 初始化。项目组合根通过 `ITencentDnConfigProvider` 提供配置，启用微信接入时填写正整数 UserActionSetId 与 32 位 SecretKey。AppID 取自实际导出脚本的 managerConfig.APPID。

包内 SDK~ 不进入 Unity 资源导入，导出处理将固定版本的 SDK 原样复制到小游戏根目录的 july-tencent-dn 中，不混淆 SDK。官方发行来源、许可证标识和完整性记录见 THIRD-PARTY-NOTICES.md。

Release 的 MiniGameBuildStep 先清空目标导出目录，再调用平台 SDK 重新导出。腾讯处理器仅在微信且配置启用时，校验模板、复制两个 JS 文件到 july-tencent-dn 子目录并注入初始化代码；不创建目录归属标记，也不负责旧产物清理。停用接入后同样执行完整导出，新产物不会包含腾讯 SDK 或初始化代码。

game.js 注入由处理器的私有 InjectGameJs 方法完成，按全新模板一次注入，沿用适配器 import 行的换行格式，并保留预下载需要的 startGame 锚点。

## 事件与调用要求

依据用户提供的《腾讯营销-IAA微信小游戏采集行为列表》DOCX 导出，更新记录包含 2026-06-25 的 SDK 1.5.11。协议来源是该离线文档，不以旧项目中较少的字段覆盖新规范。

| 事件类型 | EventName | 参数 | 项目发送方式 |
| --- | --- | --- | --- |
| `TencentDnRegisterEvent` | `REGISTER` | 无 | `track` |
| `TencentDnReactiveEvent` | `RE_ACTIVE` | `backFlowDay` | `track` |
| `TencentDnLoadFinishEvent` | `LOAD_FINISH` | 无 | `track` |
| `TencentDnLevelEnterEvent` | `LEVEL_ENTER` | 关卡信息、可选关卡名 | `track` |
| `TencentDnLevelExitEvent` | `LEVEL_EXIT` | 关卡信息、循环统计 | `track` |
| `TencentDnLevelLoseEvent` | `LEVEL_LOSE` | 关卡信息、循环统计 | `track` |
| `TencentDnLevelPassEvent` | `LEVEL_PASS` | 关卡信息、循环统计 | `track` |
| `TencentDnAdPlacementShowEvent` | `AD_PLACEMENT_SHOW` | `ad_placement_name` | `track` |
| `TencentDnAdClickEvent` | `AD_CLICK` | `ad_placement_name` | `track` |
| `TencentDnAdVideoFinishEvent` | `AD_VIDEO_FINISH` | `ad_placement_name` | `track` |
| `TencentDnTutorialStartEvent` | `TUTORIAL_START` | 无 | `track` |
| `TencentDnTutorialFinishEvent` | `TUTORIAL_FINISH` | 无 | `track` |

发送入口按已确认的统一约定序列化 `evt.ToParams()`，经 jslib 解析后调用 `sdk.track(evt.EventName, parameters)`，事件名取文档标题括号中的值。文档虽然为注册、完成引导展示了专用便捷方法，本包仍按项目要求通过 track 发送 `REGISTER` 与 `TUTORIAL_FINISH`，不增加特殊方法分发。真实 SDK 的 track 接收检查已在本地微信 API 替身下通过，远端采集仍需用项目凭证验收。

`REGISTER` 不携带业务参数。项目根据后台结果确认新注册用户，在 OpenID 绑定成功后主动调用 `TencentDnAnalytics.Track(new TencentDnRegisterEvent())`；注册判断、触发时机与避免重复上报均由项目负责，包不读取项目的 FirstLogin 字段或自动触发注册。

小游戏启动 `START_APP` 归 JS 初始化流程所有，不提供 C# 事件类型。已核对 SDK 1.5.11 默认开启自动采集，初始化保留 auto_track=true，不再手动补发 onAppStart。C# 在初始化失败时应明确停用该次发送；项目可用 getInitResult().inited 检查就绪状态。

SDK 最终报告失败只记录标识，不打印 SecretKey、OpenID 或完整失败载荷。第三方构造失败记录错误并返回 false，允许游戏继续启动；该返回值不代表数据上报成功。

## 字段语义

`TencentDnLevelInfo` 是行为发生时的关卡状态：`LevelId` 必填；`Progress`、`GameMode`、`ChapterId`、`CoinAmount`、`StaminaValue`、`LevelValue` 在玩法有对应概念时提供。数值使用可空类型，`null` 不产生字段，显式的 `0` 会保留。模式支持自定义文本，不限于固定枚举。

`Progress` 对应 `enter_level_id`，表示当前单关进度，范围 0–1，四舍五入到两位小数。它不是关卡序号。`LevelName` 只在进入关卡事件中对应 `enter_level_name`，不扩散到退出、失败和胜利事件。

`TencentDnLevelResult` 是一次进入到退出、失败或胜利的最小循环统计：

- `AdCount` 必填，对应 `ad_cnt`，仅统计循环内完整播放结束的激励视频次数，不包含退出界面广告。
- `DurationSeconds` 对应 `duration`，单位为秒。
- `Items` 通过构造参数传入 `TencentDnItemUsage[]`；数组在构造时复制。`null` 表示没有对应字段，空数组表示明确没有使用道具。
- 每种道具的协议值采用文档给出的 `"{item_type:4,item_num:3}"` 格式，整个 `items` 保持 `string[]`，不能转成一个 JSON 字符串或对象数组。

这些类型不拥有循环计时、广告计数、道具累计或复活流程。项目应在循环边界记录快照并重置自己的统计。

## 构造示例

```csharp
using July.Analytics.TencentDn;

var level = new TencentDnLevelInfo(
    levelId: 1,
    progress: 0.55,
    gameMode: "简单模式",
    chapterId: 1,
    coinAmount: 100,
    staminaValue: 10,
    levelValue: 1);

var result = new TencentDnLevelResult(
    level,
    adCount: 1,
    durationSeconds: 60,
    items: new[]
    {
        new TencentDnItemUsage(4, 3),
        new TencentDnItemUsage(22, 2)
    });

var evt = new TencentDnLevelLoseEvent(result);
var eventName = evt.EventName;
var parameters = evt.ToParams();
// 实际业务发生时调用 TencentDnAnalytics.Track(evt)；本示例不触发上报。
```

## 合并与跳过项

- “沉默唤起”与“回流”：文档作为同一 `RE_ACTIVE` 行为描述，因此只提供一个类型；后台判定与固定周期由项目提供，不从天数猜测是否为回流用户。
- “小游戏启动”：由 JS 初始化流程负责，按项目决定不加入 C# 事件清单。
- “初始化激励广告成功”：文档没有对应事件，暂不封装。已提供的 `AD_PLACEMENT_SHOW` 表示激励点位在界面中渲染完成，不能在广告对象初始化成功时替代上报；项目在真实点位展示处主动调用，包内不自动触发。

## 验证

仓库根目录执行：

```text
dotnet run --project Tests/TencentDnEvents/TencentDnEventsChecks.csproj
dotnet build Tests/TencentDnEvents/RuntimeCompatibility.csproj
dotnet run --project Tests/TencentDnIntegration/RuntimeChecks.csproj
dotnet run --project Tests/TencentDnIntegration/ExportChecks.csproj -- Packages/com.july.analytics.tencentdn Temp/TencentDnExportChecks
node Tests/TencentDnIntegration/verify-bridge.mjs Packages/com.july.analytics.tencentdn
node --experimental-vm-modules Tests/TencentDnIntegration/verify-sdk.mjs Packages/com.july.analytics.tencentdn
```

检查使用真实 `IBIEvent` 契约与本包源码，覆盖事件协议、关闭及非微信分支不改写新产物、启用时的 SDK 复制、全新模板的注入顺序与行尾保留、原生符号与 jslib 对应关系、JSON 解析和 SDK 返回码。兼容构建使用 C# 9 / .NET Standard 2.1；本地 SDK 检查使用微信 API 替身，不访问网络。这些检查不代表微信真机或远端上报验收。
