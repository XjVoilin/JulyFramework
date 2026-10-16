using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static July.AssetCheck.AssetCheckStandardFields;

namespace July.AssetCheck
{
    internal static class ProjectStandardsView
    {
        internal static void Build(VisualElement line, PlatformStandards standards, Action<Action<PlatformStandards>> edit)
        {
            AddChoice(line, "Color Space", new List<ColorSpace> { ColorSpace.Gamma, ColorSpace.Linear },
                standards.project.colorSpace, v => edit(s => s.project.colorSpace = v));
            AddChoice(line, "Graphics API 选择", new List<string> { "手动指定", "自动选择" },
                standards.project.automaticGraphicsApi ? "自动选择" : "手动指定", v => edit(s => s.project.automaticGraphicsApi = v == "自动选择"));
            ProjectGraphicsApiView.Build(line, standards.project, edit);
        }
    }
}
