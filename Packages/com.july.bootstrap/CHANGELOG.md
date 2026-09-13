# 更新记录

## 0.3.0 - 2026-09-13

- LaunchStore 替代 LaunchInfoStore，构造时接收项目配置资产引用，并保留独立就绪的启动查询结果。
- 项目入口在配置流水线前注册唯一的 LaunchStore；移除 BootArchStep 重复创建入口，Bootstrap.Configure 提前检查接入契约，方法参数不变。
- 两项目通过 Store 交接 GameConfig，可删除项目 SeedServices；不引入通用对象字典、自动释放或配置复制。
- 保留查询重试、模块注册顺序、平台延迟初始化和启动画面完成时机；此项改变 AOT 接口，升级后需重新全量构建。

## 0.2.0 - 2026-09-13

- 将 BootstrapConfig.Release 改为 Deployment，Inspector 显示“部署配置”；同步启动步骤与测试，依赖 Release 0.4.0。
- 此次为配置 API 和序列化字段重命名；消费项目需同步迁移配置资产，并重新全量构建主包。

## 0.1.0 - 2026-09-12

- 首次发布可选的 July.Launch 标准启动实现，固定公共步骤与执行顺序，项目提供配置、IBootstrapView 和 HotUpdateRegistrar。
- BootstrapConfig 组装模块配置；LaunchInfoStore 保存只读启动结果，步骤仅接收本阶段所需输入。
- 提供首帧、基础模块、并行登录与发布信息、资源初始化、标签下载、程序集装载、业务注册及启动、平台延迟初始化步骤。
- 支持启动取消、失败交互及统计；保留已就绪系统更新和平台服务延迟处理。
- 资源下载使用框架代码标签及项目 StartupDownloadTags；Player 读取构建清单，并行读取 AOT 元数据后按顺序装载代码。
- 热更名单仅在 HybridCLR Settings 维护；项目只配置补充 AOT 元数据名单和 Registrar 定位。Launch 场景由 Unity 构建场景列表携带。
- 依赖 Launch 0.2.0、Platform 0.4.0 与 Release 0.3.0；需要迁移项目接入并重新完整构建。
