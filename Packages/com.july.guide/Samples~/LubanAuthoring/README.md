# Luban 配置适配示例

复制作者表、Defines 和配置文件到项目的 Luban 流程，生成 `guide_sample.Tables`。
`SampleGuideSystem.cs.txt` 演示计划与条件注册，复制后由项目派生类实现 CreateStepProcedure。

作者表中的 Confirm、ClickTarget、表现参数表均为示例项目自定义内容，不是 Guide 核心协议。
项目 Procedure 在执行时读取 ParamId 对应配置；WindowId、文字键和 TargetId 如何解释由项目决定。
目标编号只有在项目确实提供定位能力时才有意义；不要把它交给 GuideSystem 注册。

Procedure 使用 July UI 打开普通项目窗口，通过窗口方法更新提示、观察交互，在 finally 中关闭。
确认只代表一次交互；业务成功由 Procedure 观察业务 System。跳过请求连接 Context.RequestSkip。
本地化直接使用项目已有服务。框架不再提供 GuideConfirmProcedure 或 GuideClickTargetProcedure。
本示例是配置适配模板，不是带默认窗口和目标定位的可直接运行教学。
