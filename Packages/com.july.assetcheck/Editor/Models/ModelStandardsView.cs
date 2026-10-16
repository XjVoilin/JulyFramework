using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static July.AssetCheck.AssetCheckStandardFields;

namespace July.AssetCheck
{
    internal static class ModelStandardsView
    {
        internal static void Build(VisualElement line, PlatformStandards standards, Action<Action<PlatformStandards>> edit)
        {
            AddPermission(line, "灯光导入", standards.model.allowLights, v => edit(s => s.model.allowLights = v));
            AddPermission(line, "相机导入", standards.model.allowCameras, v => edit(s => s.model.allowCameras = v));
            AddPermission(line, "Read/Write", standards.model.allowReadWrite, v => edit(s => s.model.allowReadWrite = v));
            AddPermission(line, "自动碰撞体", standards.model.allowGeneratedColliders, v => edit(s => s.model.allowGeneratedColliders = v));
            AddInt(line, "单 Mesh 面数上限", standards.model.maxTrianglesPerMesh, v => edit(s => s.model.maxTrianglesPerMesh = v));
        }
    }
}
