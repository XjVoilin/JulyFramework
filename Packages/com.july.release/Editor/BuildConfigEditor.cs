using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    [CustomEditor(typeof(BuildConfig))]
    public sealed class BuildConfigEditor : UnityEditor.Editor
    {
        bool _integration, _aot, _policies;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("bootConfig", "运行配置");
            Field("cloudUrl", "COS 项目根地址");
            Field("planVersion", "内容版本");
            Field("baseDefines", "公共编译宏");
            _integration = EditorGUILayout.Foldout(_integration, "构建接入（首次配置）", true);
            if (_integration)
            {
                Field("collectorSettings", "YooAsset 收集配置");
                EditorGUILayout.LabelField("DLL 分组约定", "HotFix / AOTMeta");
                var config = (BuildConfig)target;
                if (config.bootConfig is IReleaseResourceConfig shared)
                {
                    EditorGUILayout.HelpBox("资源包、标签和必需 AOT 程序集读取运行配置中的共享数据。", MessageType.Info);
                    if (GUILayout.Button("定位共享配置")) Selection.activeObject = config.bootConfig;
                }
                else Field("resources", "构建资源参数");
                EditorGUILayout.HelpBox("DLL 目录读取所选分组；泛型引用路径读取 HybridCLR 设置。接入检查只读，不自动改写资源收集规则。", MessageType.Info);
            }
            _aot = EditorGUILayout.Foldout(_aot, "AOT", true);
            if (_aot) Field("aot", "源码校验");
            EditorGUILayout.HelpBox("平台产物：../Build/平台/核心版本\n本机 AOT：../AOTBackup/工程目录名\nCOS 工具：Release 包内置\nCOS 凭证：Tools/coscli/.cos.yaml\n以上由框架约定，无需项目配置。", MessageType.Info);
            _policies = EditorGUILayout.Foldout(_policies, "可选构建策略", true);
            if (_policies)
            {
                Policy("sharedBundles", "按目录合并共享包");
                Field("launchFont", "替换启动字体（可留空）");
                Field("splashImage", "启动背景图（可留空）");
                Field("disableUnitySplash", "关闭 Unity 启动画面");
                Field("maxPreloadCount", "预下载数量上限");
                Field("maxPreloadBytes", "预下载字节上限");
            }
            serializedObject.ApplyModifiedProperties();
        }

        void Field(string name, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label), true);
        void Policy(string name, string label)
        {
            var property = serializedObject.FindProperty(name);
            var enabled = property.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(enabled, new GUIContent(label));
            if (!enabled.boolValue) return;
            using (new EditorGUI.IndentLevelScope())
            {
                var child = property.Copy();
                var end = property.GetEndProperty();
                if (child.NextVisible(true))
                    do
                    {
                        if (SerializedProperty.EqualContents(child, end)) break;
                        if (child.name != "enabled") EditorGUILayout.PropertyField(child, true);
                    } while (child.NextVisible(false));
            }
        }
    }
}
