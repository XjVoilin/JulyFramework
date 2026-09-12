# 更新记录

## 0.1.0 - 2026-09-12

- 首次发布可选的 July.Launch 标准启动实现，固定公共步骤与执行顺序，项目提供配置、IBootstrapView 和 HotUpdateRegistrar。
- BootstrapConfig 组装模块配置；LaunchInfoStore 保存只读启动结果，步骤仅接收本阶段所需输入。
- 提供首帧、基础模块、并行登录与发布信息、资源初始化、标签下载、程序集装载、业务注册及启动、平台延迟初始化步骤。
- 支持启动取消、失败交互及统计；保留已就绪系统更新和平台服务延迟处理。
- 资源下载使用框架代码标签及项目 StartupDownloadTags；Player 读取构建清单，并行读取 AOT 元数据后按顺序装载代码。
- 热更名单仅在 HybridCLR Settings 维护；项目只配置补充 AOT 元数据名单和 Registrar 定位。Launch 场景由 Unity 构建场景列表携带。
- 依赖 Launch 0.2.0、Platform 0.4.0 与 Release 0.3.0；需要迁移项目接入并重新完整构建。
