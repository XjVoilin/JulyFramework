# July Launch

本包提供启动管线、启动步骤编排与 `JulyGameEntry`。标准启动步骤由可选的
`com.july.bootstrap` 提供，具体场景和游戏配置由项目维护。

## 编辑器启动入口

Unity 主工具栏播放按钮左侧提供 **▶ Launch**，菜单入口为
`July/Launch/Play Launch Scene`。

- 点击时通过 AssetDatabase 在 `Assets` 下检索场景，再严格匹配文件名 `Launch`（区分大小写）。
- 唯一匹配时启动，不需要配置路径；场景可以移动到 `Assets` 中的其它目录。
- 找不到或存在多个 `Launch.unity` 时提示原因及重名路径，不猜测启动目标。
- 启动前允许保存或取消修改；播放中、正在切换播放、编译或导入时禁用。
- 使用 Unity 的 `playModeStartScene` 临时覆盖本次启动目标，退出播放或取消进入后恢复
  原设置。临时状态保存在 SessionState 中以跨脚本重载，普通播放行为保持原状。
- 不修改构建场景列表，不向场景添加组件，不增加 Player 代码。

主工具栏接入按 Unity **2022.3.62f2** 的内部 Toolbar 结构实现，适配集中在
`Editor/LaunchToolbar.cs`；升级 Unity 后需要验证工具栏结构及布局。菜单命令与
工具栏共用启动逻辑。
