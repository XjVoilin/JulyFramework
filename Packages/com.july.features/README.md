# July Features

July Features 从项目提供的 JSON 读取功能的可用条件。每个功能一行，默认要求全部条件满足。

```json
[
  {
    "id": 101,
    "conditions": [{ "type": 1, "arg": "4" }, { "type": 2, "arg": "3" }],
    "negate": false,
    "prefabKey": "entry/example"
  }
]
```

每行填写整数功能 ID `id`。有条件时填写整数 `type`，它由项目配表枚举导出，与 `IFeatureRule.Id` 匹配。内置平台规则的 ID 为 1，它在求值时把参数转成整数并和当前 `IPlatformSystem.PlatformType` 比较。示例中的平台参数 "4" 表示抖音。后续规则编号从 2 起顺排，示例中的规则 2 由项目定义。`conditions` 必填。无条件限制的功能填写 `[]`。功能行的 `negate` 默认为 `false`。设为 `true` 时，先判断所有条件是否同时满足，再对这个结果取反。July 通过 `ISerializeSystem` 读取所需字段，项目扩展字段可继续由项目读取。

项目继承 `FeatureSystemBase`。有自定义规则时，在 `CreateProjectRules()` 返回规则实例。基类首次加载时按 ID 注册内置规则和项目规则，重复编号会报错。查询执行到某条条件时，基类根据 `type` 查找规则，并通过 `IsSatisfied(string arg)` 将该条条件的参数交给规则解析和求值。

```csharp
protected override IEnumerable<IFeatureRule> CreateProjectRules()
{
    yield return new LoginDaysRule(GetStore<AccountStore>());
}
```

```csharp
arch.RegisterSystem(new ProjectFeatureSystem());

var features = arch.GetSystem<IFeatureSystem>();
features.LoadConfig(json);
bool available = features.CanUse(featureId);
```

先注册平台、序列化和功能系统，再加载配置。`id` 和 `type` 缺省时为 `-1`，这个值保留给未填写的编号。加载时检查功能 ID，查询执行到某条条件时检查它的 `type`。查询读取当前状态。功能不存在时返回 `false`。空条件数组视为全部满足，取反后为 `false`。界面根据可用结果决定如何呈现。首次成功加载前查询会抛出 `InvalidOperationException`。

包内 EditMode 测试可通过 Unity 项目清单中的 `testables` 启用。
