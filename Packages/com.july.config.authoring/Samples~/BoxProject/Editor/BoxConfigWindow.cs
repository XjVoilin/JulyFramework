using UnityEditor;
using UnityEngine;

namespace July.Config.Authoring.Samples
{
    public sealed class BoxConfigWindow : ConfigAuthoringWindow
    {
        [MenuItem("BoxProject/配置表生成")]
        private static void Open() => OpenWindow<BoxConfigWindow>("盒子配置表");

        protected override void DrawContent()
        {
            using (new EditorGUI.DisabledScope(!ConfigGeneration.CanGenerate))
                if (GUILayout.Button("只生成公共配置")) ConfigGeneration.Generate("Common");
            DrawDefaultContent();
        }
    }
}
