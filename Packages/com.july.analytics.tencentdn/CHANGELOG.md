# 更新记录

## 0.1.0 - 2026-10-27

- 删除 SDK~ 下重复的来源记录文件，版本、下载来源及完整性信息统一维护于 THIRD-PARTY-NOTICES.md。

- game.js 注入合并为 TencentDnExportProcessor 的私有 InjectGameJs，移除独立文本处理类、注入标记和旧产物更新/撤销逻辑，按全量导出后的新模板一次注入。

- 新增空业务参数的 TencentDnRegisterEvent，通过 track 发送 REGISTER，注册判断和触发由项目负责。

- 新增 TencentDnAdPlacementShowEvent，使用 AD_PLACEMENT_SHOW 和必填数值字段 ad_placement_name，供项目在激励点位渲染完成时主动上报。

- 保留 july-tencent-dn 子目录，移除目录归属标记及已有产物清理分支；启用时复制并注入，停用时跳过，旧产物由 Release 全量导出前统一清理。

- 保留 game.js 原始文本，新插入代码沿用模板行尾；检查 LF、CRLF、混合行尾、无末尾换行、真实微信模板及 SDK 构造时自动触发 START_APP。

- SDK 文件统一为 dn-sdk-minigame.js，后续兼容升级只替换文件及维护来源记录，无需修改代码引用。

- 新增 SDK 配置契约、Release 导出适配及官方 SDK 1.5.11，保留自动采集，不重复手动上报启动。

- 新增腾讯 IAA 事件数据类型，使用现有 `IBIEvent` 契约，独立于项目数数打点。
- 关卡行为覆盖文档中的必报及有则必报字段，保留道具字符串数组格式和最小循环统计语义。
- 将沉默唤起与回流合并为文档定义的 `RE_ACTIVE`；缺少协议的激励广告初始化成功事件暂不封装。
- 启动上报归 JS 初始化流程，C# 不提供启动事件；其余事件统一供项目通过 track 发送。
- 静态 TencentDnAnalytics 与通用 jslib 下沉到包内，提供 Configure、Track(IBIEvent)、SetOpenId；原生声明为私有，使用 July_TencentDn_ 符号。
- 项目在登录和业务发生处主动调用，不引入 TencentDnSystem、登录监听或业务去重状态，不自动注册统计通道。
- 包内 link.xml 保留供热更代码调用的 AOT 实现。
