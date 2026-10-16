using System;
using System.Linq;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal static class ProjectGraphicsApiView
    {
        internal static void Build(VisualElement parent, ProjectStandards standards, Action<Action<PlatformStandards>> edit)
        {
            var list = new VisualElement();
            list.SetEnabled(!standards.automaticGraphicsApi);
            parent.Add(list);
            var choices = Enum.GetValues(typeof(GraphicsDeviceType)).Cast<GraphicsDeviceType>()
                .Where(api => api != GraphicsDeviceType.Null).Distinct().ToList();
            for (var index = 0; index < standards.graphicsApis.Length; index++)
            {
                var position = index;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                var field = new PopupField<GraphicsDeviceType>("API " + (index + 1), choices.Where(api => api == standards.graphicsApis[position] || !standards.graphicsApis.Contains(api)).ToList(), standards.graphicsApis[index]);
                field.RegisterValueChangedCallback(change => edit(s => s.project.graphicsApis[position] = change.newValue));
                row.Add(field);
                var up = new Button(() => edit(s =>
                {
                    var values = s.project.graphicsApis;
                    var previous = values[position - 1];
                    values[position - 1] = values[position];
                    values[position] = previous;
                })) { text = "↑" };
                up.SetEnabled(index > 0);
                row.Add(up);
                var remove = new Button(() => edit(s => s.project.graphicsApis = s.project.graphicsApis
                    .Where((_, i) => i != position).ToArray())) { text = "移除" };
                remove.SetEnabled(standards.graphicsApis.Length > 1);
                row.Add(remove);
                list.Add(row);
            }
            var available = choices.Except(standards.graphicsApis).ToList();
            var add = new Button(() => edit(s => s.project.graphicsApis = s.project.graphicsApis.Concat(new[] { available[0] }).ToArray()))
                { text = "添加 API", tooltip = "按顺序比较项目的 Graphics API 列表；此处只修改检查标准。" };
            add.SetEnabled(available.Count > 0);
            list.Add(add);
        }
    }
}
