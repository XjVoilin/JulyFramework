# July Physics

可复用的 Unity 物理配置组件。不依赖 Arch、Scene 或游戏业务，不增加物理调度或服务生命周期。

## ColliderContactOffset

在需要独立接触偏移的物体上添加 `July/Physics/Collider Contact Offset`，在 Inspector 中配置“接触偏移”，数值随 Prefab 保存。

- `Awake` 缓存并验证同一 GameObject 上的全部三维 `Collider`；`OnEnable` 为已启用的碰撞形状应用配置。
- 不遍历子节点，不创建碰撞体，也不改变项目全局默认值。碰撞体启停由调用方决定。
- 不需要 View 或其他调用方初始化。GameObject 再激活时通过 `OnEnable` 恢复配置。
- 运行时需要切换 `Collider.enabled` 时，在组件 Awake 后调用 `SetEnabled(bool)`，组件会在启用原生形状后应用接触偏移。不要直接切换 Collider.enabled 再期待此前的偏移自动保留。
- 数值必须有限且大于零，同一物体必须有 Collider；非法配置在 `Awake` 抛出有意义的异常。
- 默认配置为 `0.01` 世界单位；具体项目应在 Prefab 上保存所需值。

Unity 2022.3 的原生形状在禁用/重新启用时不会保留实例接触偏移，因此仅在 Awake 赋值不足以保持配置。组件封装必要的启停顺序，不使用每帧轮询。

运行时动态添加 Collider 或动态改变接触偏移不属于本组件的配置范围。
