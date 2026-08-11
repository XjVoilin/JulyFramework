using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Editor
{
    /// <summary>
    /// 创建UI窗口工具
    /// 右键Project窗口 -> JulyGF/创建UIWindow
    /// </summary>
    public class CreateUIWindowEditor : EditorWindow
    {
        private const string DefaultCodePath = "Assets/Game/Scripts/Views/Windows";
        private const string DefaultPrefabPath = "Assets/Game/Res/Prefabs/UI";

        private string _windowName = "";
        private string _description = "";
        private string _codePath = DefaultCodePath;
        private string _prefabPath = DefaultPrefabPath;
        private bool _showPathOptions;

        // EditorPrefs 键名
        private const string PrefsKeyPendingClassName = "JulyGF.CreateUIWindow.PendingClassName";
        private const string PrefsKeyPendingPrefabPath = "JulyGF.CreateUIWindow.PendingPrefabPath";

        /// <summary>
        /// 右键菜单项
        /// </summary>
        [MenuItem("Assets/JulyGF/创建UIWindow", false, 0)]
        public static void ShowWindow()
        {
            CreateUIWindowEditor window = GetWindow<CreateUIWindowEditor>("创建UI窗口");
            window.minSize = new Vector2(350, 150);
            window.Show();
        }


        private void OnGUI()
        {
            // 窗口名称输入
            EditorGUILayout.LabelField("窗口名称", EditorStyles.boldLabel);
            _windowName = EditorGUILayout.TextField("名称", _windowName);
            EditorGUILayout.Space(5);

            _description = EditorGUILayout.TextField("说明(可选)", _description);
            EditorGUILayout.Space(5);

            // 路径选项复选框
            _showPathOptions = EditorGUILayout.Toggle("自定义路径", _showPathOptions);
            EditorGUILayout.Space(5);

            // 路径选择（仅在选中时显示）
            if (_showPathOptions)
            {
                _codePath = DrawPathField("代码保存路径", _codePath, "选择代码保存路径");
                EditorGUILayout.Space(5);
                _prefabPath = DrawPathField("Prefab保存路径", _prefabPath, "选择Prefab保存路径");
                EditorGUILayout.Space(5);
            }

            // 确认按钮
            GUI.enabled = !string.IsNullOrEmpty(_windowName) && _windowName.Trim().Length > 0;
            if (GUILayout.Button("创建", GUILayout.Height(30)))
            {
                CreateUIWindow();
            }

            GUI.enabled = true;
        }

        #region UI绘制方法

        /// <summary>
        /// 绘制路径选择字段
        /// </summary>
        private string DrawPathField(string label, string currentPath, string dialogTitle)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var path = EditorGUILayout.TextField("路径", currentPath);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                var selectedPath = EditorUtility.OpenFolderPanel(dialogTitle, currentPath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    path = ConvertToAssetPath(selectedPath);
                }
            }

            EditorGUILayout.EndHorizontal();
            return path;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 规范化路径（统一使用正斜杠）
        /// </summary>
        private static string NormalizePath(string path)
        {
            return path?.Replace("\\", "/") ?? string.Empty;
        }

        /// <summary>
        /// 将文件系统路径转换为Asset路径
        /// </summary>
        private string ConvertToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;

            if (fullPath.StartsWith(Application.dataPath))
            {
                return "Assets" + NormalizePath(fullPath.Substring(Application.dataPath.Length));
            }

            return NormalizePath(fullPath);
        }

        /// <summary>
        /// 获取Asset路径下的文件夹路径
        /// </summary>
        private static string GetAssetFolderPath(string basePath, string folderName)
        {
            return NormalizePath(Path.Combine(basePath, folderName));
        }

        /// <summary>
        /// 将Asset路径转换为完整文件系统路径
        /// </summary>
        private static string ToFullPath(string assetPath)
        {
            return assetPath.Replace("Assets/", Application.dataPath + "/");
        }

        /// <summary>
        /// 确保目录存在
        /// </summary>
        private static void EnsureDirectoryExists(string assetPath)
        {
            var fullPath = ToFullPath(assetPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        /// <summary>
        /// 显示错误对话框
        /// </summary>
        private void ShowErrorDialog(string message)
        {
            EditorUtility.DisplayDialog("错误", message, "确定");
        }

        #endregion

        /// <summary>
        /// 创建UI窗口
        /// </summary>
        private void CreateUIWindow()
        {
            var className = _windowName.Trim();

            if (string.IsNullOrEmpty(className))
            {
                ShowErrorDialog("窗口名称不能为空");
                return;
            }

            if (!IsValidClassName(className))
            {
                ShowErrorDialog("窗口名称包含非法字符，请使用字母、数字和下划线，且必须以字母或下划线开头");
                return;
            }

            try
            {
                var codeFilePath = CreateCodeFile(className, _description);

                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(codeFilePath);
                AssetDatabase.Refresh();

                // 保存待创建信息到 EditorPrefs（编译后不会丢失）
                EditorPrefs.SetString(PrefsKeyPendingClassName, className);
                EditorPrefs.SetString(PrefsKeyPendingPrefabPath, _prefabPath);

                Close();
            }
            catch (System.Exception ex)
            {
                ShowErrorDialog($"创建失败: {ex.Message}");
                Debug.LogError($"[CreateUIWindow] 创建失败: {ex}");
            }
        }

        /// <summary>
        /// 验证类名是否合法
        /// </summary>
        private bool IsValidClassName(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;

            // 必须以字母或下划线开头
            if (!char.IsLetter(className[0]) && className[0] != '_')
                return false;

            // 只能包含字母、数字和下划线
            return className.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        /// <summary>
        /// 创建代码文件
        /// </summary>
        private string CreateCodeFile(string className, string desc)
        {
            var folderPath = GetAssetFolderPath(_codePath, className);
            var filePath = NormalizePath(Path.Combine(folderPath, $"{className}.cs"));
            var fullPath = ToFullPath(filePath);

            EnsureDirectoryExists(folderPath);

            var codeContent = GenerateCodeContent(className, desc);
            File.WriteAllText(fullPath, codeContent);

            Debug.Log($"[CreateUIWindow] 代码文件已创建: {filePath}");
            return filePath;
        }

        /// <summary>
        /// 生成代码内容
        /// </summary>
        private string GenerateCodeContent(string className, string desc)
        {
            var title = string.IsNullOrEmpty(desc) ? className : desc;
            return $@"using July.UI;

/// <summary>
/// {title}
/// </summary>
public class {className} : UIView
{{
    protected override void OnBeforeOpen()
    {{
        base.OnBeforeOpen();
    }}

    protected override void OnClose()
    {{
        base.OnClose();
    }}
}}
";
        }

        /// <summary>
        /// 创建Prefab文件并挂载脚本（脚本重新加载后调用）
        /// </summary>
        private static void CreatePrefabAndAttachScript(string prefabPath, string className)
        {
            try
            {
                var folderPath = GetAssetFolderPath(prefabPath, className);
                var filePath = NormalizePath(Path.Combine(folderPath, $"{className}.prefab"));

                EnsureDirectoryExists(folderPath);

                // 在场景中创建临时Canvas（让Unity正确识别UI上下文）
                // 注意：必须在场景中创建，而不是在内存中，这样Unity才能正确识别
                var tempCanvas = new GameObject("TempCanvas");
                var canvas = tempCanvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                tempCanvas.AddComponent<CanvasScaler>();
                tempCanvas.AddComponent<GraphicRaycaster>();

                // 创建UI GameObject作为Canvas的子对象
                var prefabObj = CreatePrefabGameObject(className);
                prefabObj.transform.SetParent(tempCanvas.transform, false);

                // 挂载脚本
                AttachScriptToPrefab(prefabObj, className);

                // 标记场景为dirty（让Unity知道场景有变化）
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

                // 只保存子对象为prefab（不包含Canvas）
                var prefabAsset = PrefabUtility.SaveAsPrefabAsset(prefabObj, filePath);

                // 清理临时对象（从场景中删除）
                DestroyImmediate(tempCanvas);

                // 刷新资源数据库，确保prefab可以被找到
                AssetDatabase.Refresh();

                // 延迟选中prefab（等待资源刷新完成）
                EditorApplication.delayCall += () =>
                {
                    var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(filePath);
                    if (prefabAsset != null)
                    {
                        // 选中prefab
                        Selection.activeObject = prefabAsset;
                        // 在Project窗口中高亮显示并展开文件夹
                        EditorGUIUtility.PingObject(prefabAsset);
                        // 聚焦Project窗口
                        EditorUtility.FocusProjectWindow();
                        // 展开文件夹路径
                        ExpandFolderInProjectWindow(folderPath);
                    }
                    else
                    {
                        Debug.LogWarning($"[CreateUIWindow] 无法加载Prefab: {filePath}");
                    }
                };

                Debug.Log($"[CreateUIWindow] Prefab文件已创建并挂载脚本: {filePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CreateUIWindow] 创建Prefab失败: {ex}");
            }
        }

        /// <summary>
        /// 创建Prefab GameObject（包含必要组件）
        /// 注意：此方法创建的GameObject会被挂载到临时Canvas下，然后只保存子对象为prefab
        /// </summary>
        private static GameObject CreatePrefabGameObject(string className)
        {
            // 根节点：逻辑节点
            var root = new GameObject(className, typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // CanvasGroup（框架逻辑需要）
            var canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            // ================================
            // 子节点：UI 占位节点（关键）
            // ================================
            var placeholder = new GameObject("----占位----", typeof(RectTransform));
            placeholder.transform.SetParent(root.transform, false);
            placeholder.layer = LayerMask.NameToLayer("UI");

            var image = placeholder.AddComponent<Image>();
            image.color = Color.clear; // 不影响视觉
            image.raycastTarget = false;

            return root;
        }

        /// <summary>
        /// 挂载脚本到Prefab（简化版本，不需要太严谨的逻辑）
        /// </summary>
        private static void AttachScriptToPrefab(GameObject prefab, string className)
        {
            var scriptType = FindScriptType(className);
            if (scriptType == null)
            {
                Debug.LogWarning($"[CreateUIWindow] 无法找到脚本类型: {className}，请手动挂载");
                return;
            }

            // 如果已存在则跳过
            if (prefab.GetComponent(scriptType) != null)
            {
                return;
            }

            prefab.AddComponent(scriptType);
            EditorUtility.SetDirty(prefab);
        }

        /// <summary>
        /// 在Project窗口中展开文件夹路径
        /// </summary>
        private static void ExpandFolderInProjectWindow(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;

            // 将路径分割成各个文件夹
            var pathParts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";

            // 逐级展开每个文件夹
            foreach (var part in pathParts)
            {
                if (string.IsNullOrEmpty(currentPath))
                {
                    currentPath = part;
                }
                else
                {
                    currentPath = NormalizePath(Path.Combine(currentPath, part));
                }

                // 加载文件夹资源
                var folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(currentPath);
                if (folderAsset != null)
                {
                    // Ping文件夹以展开它
                    EditorGUIUtility.PingObject(folderAsset);
                }
            }
        }

        /// <summary>
        /// 查找脚本类型
        /// </summary>
        private static System.Type FindScriptType(string className)
        {
            return System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(className))
                .FirstOrDefault(type => type != null);
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnCompilationFinished()
        {
            // 从 EditorPrefs 读取待创建信息（编译后不会丢失）
            var className = EditorPrefs.GetString(PrefsKeyPendingClassName, string.Empty);
            var prefabPath = EditorPrefs.GetString(PrefsKeyPendingPrefabPath, string.Empty);

            // 检查是否有待创建的Prefab
            if (string.IsNullOrEmpty(className))
            {
                return;
            }

            // 创建Prefab并挂载脚本
            CreatePrefabAndAttachScript(prefabPath, className);

            // 清空待创建信息
            EditorPrefs.DeleteKey(PrefsKeyPendingClassName);
            EditorPrefs.DeleteKey(PrefsKeyPendingPrefabPath);
        }
    }
}