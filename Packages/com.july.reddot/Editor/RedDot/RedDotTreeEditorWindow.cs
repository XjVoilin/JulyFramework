using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using July.RedDot;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.RedDot.Editor
{
    /// <summary>
    /// 红点树可视化编辑器窗口 (UI Toolkit 版本)
    /// 功能：
    /// 1. 可视化显示红点树结构
    /// 2. 添加/删除/编辑节点
    /// 3. 验证配置
    /// 4. 导出为代码
    /// </summary>
    public class RedDotTreeEditorWindow : EditorWindow
    {
        private RedDotTreeConfig _config;
        private RedDotNodeDefinition _selectedNode;
        private List<string> _validationErrors = new List<string>();
        private Dictionary<string, int> _keyToIdMap = new Dictionary<string, int>(); // Key 到 TreeView ID 的映射

        // UI 元素引用
        private ObjectField _configField;
        private ToolbarSearchField _searchField;
        private Toggle _showOnlyErrorsToggle;
        private TreeView _treeView;
        private VisualElement _detailPanel;
        private Label _statusLabel;
        private Label _nodeCountLabel;
        private VisualElement _validationPanel;
        private VisualElement _statisticsPanel;

        // 快速添加表单
        private TextField _newKeyField;
        private TextField _newParentField;
        private TextField _newModuleField;
        private EnumField _newTypeField;

        [MenuItem("JulyGF/RedDot/红点树编辑器", false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<RedDotTreeEditorWindow>();
            window.titleContent = new GUIContent("红点树编辑器");
            window.minSize = new Vector2(900, 600);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var path = AssetDatabase.GUIDToAssetPath("f7e6eab565d6bb941bd25e84a4fd7010");
            // 加载 USS 样式文件
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // 应用内联基础样式（确保即使 USS 未加载也能正常显示）
            ApplyBaseStyles(root);

            // 构建 UI
            BuildUI(root);

            // 加载上次的配置
            LoadLastConfig();
        }

        private void ApplyBaseStyles(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.flexDirection = FlexDirection.Column;
        }

        #region UI 构建

        private void BuildUI(VisualElement root)
        {
            root.AddToClassList("root");

            // 工具栏
            var toolbar = CreateToolbar();
            root.Add(toolbar);

            // 主内容区
            var mainContent = CreateMainContent();
            root.Add(mainContent);

            // 状态栏
            var statusBar = CreateStatusBar();
            root.Add(statusBar);
        }

        private VisualElement CreateToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("toolbar");

            // 配置文件选择
            _configField = new ObjectField();
            _configField.objectType = typeof(RedDotTreeConfig);
            _configField.style.width = 200;
            _configField.RegisterValueChangedCallback(evt =>
            {
                _config = evt.newValue as RedDotTreeConfig;
                _selectedNode = null;
                OnConfigChanged();
            });
            toolbar.Add(_configField);

            toolbar.Add(new ToolbarSpacer { style = { width = 10 } });

            // 搜索框
            _searchField = new ToolbarSearchField();
            _searchField.style.width = 150;
            _searchField.RegisterValueChangedCallback(evt => RefreshTreeView());
            toolbar.Add(_searchField);

            toolbar.Add(new ToolbarSpacer { style = { width = 10 } });

            // 只显示错误
            _showOnlyErrorsToggle = new Toggle("只显示异常");
            _showOnlyErrorsToggle.RegisterValueChangedCallback(evt => RefreshTreeView());
            toolbar.Add(_showOnlyErrorsToggle);

            toolbar.Add(new ToolbarSpacer { flex = true });

            // 按钮组
            var validateBtn = new ToolbarButton(ValidateConfig) { text = "验证" };
            toolbar.Add(validateBtn);

            var expandBtn = new ToolbarButton(() => _treeView?.ExpandAll()) { text = "展开全部" };
            toolbar.Add(expandBtn);

            var collapseBtn = new ToolbarButton(() => _treeView?.CollapseAll()) { text = "折叠全部" };
            toolbar.Add(collapseBtn);

            toolbar.Add(new ToolbarSpacer { style = { width = 10 } });

            var exportBtn = new ToolbarButton(ExportCode) { text = "导出代码" };
            exportBtn.AddToClassList("export-button");
            toolbar.Add(exportBtn);

            return toolbar;
        }

        private VisualElement CreateMainContent()
        {
            var mainContent = new VisualElement();
            mainContent.AddToClassList("main-content");
            mainContent.style.flexGrow = 1;
            mainContent.style.flexDirection = FlexDirection.Row;

            // 左侧面板（树视图）
            var leftPanel = CreateLeftPanel();
            mainContent.Add(leftPanel);

            // 分隔线
            var splitter = new VisualElement();
            splitter.AddToClassList("splitter");
            splitter.style.width = 2;
            splitter.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            mainContent.Add(splitter);

            // 右侧面板（详情）
            var rightPanel = CreateRightPanel();
            mainContent.Add(rightPanel);

            return mainContent;
        }

        private VisualElement CreateLeftPanel()
        {
            var leftPanel = new VisualElement();
            leftPanel.AddToClassList("left-panel");
            leftPanel.style.width = Length.Percent(50);
            leftPanel.style.flexDirection = FlexDirection.Column;
            leftPanel.style.paddingLeft = leftPanel.style.paddingRight = 8;
            leftPanel.style.paddingTop = leftPanel.style.paddingBottom = 8;

            // 标题
            var header = new Label("红点树结构");
            header.AddToClassList("panel-header");
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.paddingBottom = 8;
            leftPanel.Add(header);

            // 快速添加区
            var quickAdd = CreateQuickAddPanel();
            leftPanel.Add(quickAdd);

            // 树视图容器
            var treeContainer = new VisualElement();
            treeContainer.AddToClassList("tree-container");
            treeContainer.style.flexGrow = 1;
            treeContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            treeContainer.style.borderTopLeftRadius = treeContainer.style.borderTopRightRadius = 4;
            treeContainer.style.borderBottomLeftRadius = treeContainer.style.borderBottomRightRadius = 4;
            treeContainer.style.paddingLeft = treeContainer.style.paddingRight = 4;
            treeContainer.style.paddingTop = treeContainer.style.paddingBottom = 4;

            _treeView = new TreeView();
            _treeView.AddToClassList("tree-view");
            _treeView.makeItem = MakeTreeItem;
            _treeView.bindItem = BindTreeItem;
            _treeView.selectionChanged += OnTreeSelectionChanged;
            _treeView.style.flexGrow = 1;

            treeContainer.Add(_treeView);
            leftPanel.Add(treeContainer);

            return leftPanel;
        }

        private VisualElement CreateQuickAddPanel()
        {
            var container = new VisualElement();
            container.AddToClassList("quick-add-panel");
            container.style.backgroundColor = new Color(0.23f, 0.23f, 0.23f);
            container.style.borderTopLeftRadius = container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = container.style.borderBottomRightRadius = 4;
            container.style.paddingLeft = container.style.paddingRight = 8;
            container.style.paddingTop = container.style.paddingBottom = 8;
            container.style.marginBottom = 8;

            // 第一行
            var row1 = new VisualElement();
            row1.AddToClassList("quick-add-row");
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.alignItems = Align.Center;
            row1.style.marginBottom = 4;

            row1.Add(new Label("快速添加") { style = { width = 60 } });

            _newKeyField = new TextField { style = { width = 100 } };
            _newKeyField.SetPlaceholderText("Key");
            row1.Add(_newKeyField);

            row1.Add(new Label("→") { style = { width = 20, unityTextAlign = TextAnchor.MiddleCenter } });

            _newParentField = new TextField { style = { width = 80 } };
            _newParentField.SetPlaceholderText("父节点");
            row1.Add(_newParentField);

            _newTypeField = new EnumField(RedDotType.Normal) { style = { width = 70 } };
            row1.Add(_newTypeField);

            var addBtn = new Button(() => AddNewNode()) { text = "+" };
            addBtn.AddToClassList("add-button");
            addBtn.style.width = 25;
            addBtn.style.height = 20;
            addBtn.style.backgroundColor = new Color(0.29f, 0.55f, 0.29f);
            addBtn.style.color = Color.white;
            addBtn.style.marginLeft = 4;
            row1.Add(addBtn);

            container.Add(row1);

            // 第二行
            var row2 = new VisualElement();
            row2.AddToClassList("quick-add-row");
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.alignItems = Align.Center;

            row2.Add(new Label("模块") { style = { width = 60 } });
            _newModuleField = new TextField { style = { width = 100 } };
            _newModuleField.SetPlaceholderText("模块名");
            row2.Add(_newModuleField);

            row2.Add(new Label("(Key → 父节点)") { style = { color = Color.gray, fontSize = 10 } });

            container.Add(row2);

            return container;
        }

        private VisualElement CreateRightPanel()
        {
            var rightPanel = new VisualElement();
            rightPanel.AddToClassList("right-panel");
            rightPanel.style.flexGrow = 1;
            rightPanel.style.flexDirection = FlexDirection.Column;
            rightPanel.style.paddingLeft = rightPanel.style.paddingRight = 8;
            rightPanel.style.paddingTop = rightPanel.style.paddingBottom = 8;

            // 标题
            var header = new Label("节点详情");
            header.AddToClassList("panel-header");
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.paddingBottom = 8;
            rightPanel.Add(header);

            // 滚动视图
            var scrollView = new ScrollView();
            scrollView.style.flexGrow = 1;

            // 详情面板
            _detailPanel = new VisualElement();
            _detailPanel.AddToClassList("detail-panel");
            _detailPanel.style.backgroundColor = new Color(0.23f, 0.23f, 0.23f);
            _detailPanel.style.borderTopLeftRadius = _detailPanel.style.borderTopRightRadius = 4;
            _detailPanel.style.borderBottomLeftRadius = _detailPanel.style.borderBottomRightRadius = 4;
            _detailPanel.style.paddingLeft = _detailPanel.style.paddingRight = 12;
            _detailPanel.style.paddingTop = _detailPanel.style.paddingBottom = 12;
            _detailPanel.style.marginBottom = 8;
            scrollView.Add(_detailPanel);

            // 验证问题面板
            _validationPanel = new VisualElement();
            _validationPanel.AddToClassList("validation-panel");
            _validationPanel.style.backgroundColor = new Color(0.29f, 0.23f, 0.23f);
            _validationPanel.style.borderTopLeftRadius = _validationPanel.style.borderTopRightRadius = 4;
            _validationPanel.style.borderBottomLeftRadius = _validationPanel.style.borderBottomRightRadius = 4;
            _validationPanel.style.paddingLeft = _validationPanel.style.paddingRight = 8;
            _validationPanel.style.paddingTop = _validationPanel.style.paddingBottom = 8;
            _validationPanel.style.marginBottom = 8;
            scrollView.Add(_validationPanel);

            // 统计信息面板
            _statisticsPanel = new VisualElement();
            _statisticsPanel.AddToClassList("statistics-panel");
            _statisticsPanel.style.backgroundColor = new Color(0.23f, 0.23f, 0.23f);
            _statisticsPanel.style.borderTopLeftRadius = _statisticsPanel.style.borderTopRightRadius = 4;
            _statisticsPanel.style.borderBottomLeftRadius = _statisticsPanel.style.borderBottomRightRadius = 4;
            _statisticsPanel.style.paddingLeft = _statisticsPanel.style.paddingRight = 12;
            _statisticsPanel.style.paddingTop = _statisticsPanel.style.paddingBottom = 12;
            scrollView.Add(_statisticsPanel);

            rightPanel.Add(scrollView);

            return rightPanel;
        }

        private VisualElement CreateStatusBar()
        {
            var statusBar = new VisualElement();
            statusBar.AddToClassList("status-bar");
            statusBar.style.flexDirection = FlexDirection.Row;
            statusBar.style.paddingLeft = statusBar.style.paddingRight = 8;
            statusBar.style.paddingTop = statusBar.style.paddingBottom = 4;
            statusBar.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            statusBar.style.borderTopWidth = 1;
            statusBar.style.borderTopColor = new Color(0.1f, 0.1f, 0.1f);

            _statusLabel = new Label("✓ 就绪");
            _statusLabel.AddToClassList("status-label");
            statusBar.Add(_statusLabel);

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            statusBar.Add(spacer);

            _nodeCountLabel = new Label("节点总数: 0");
            statusBar.Add(_nodeCountLabel);

            return statusBar;
        }

        #endregion

        #region TreeView

        private VisualElement MakeTreeItem()
        {
            var item = new VisualElement();
            item.AddToClassList("tree-item");
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.paddingLeft = item.style.paddingRight = 4;
            item.style.paddingTop = item.style.paddingBottom = 2;
            item.style.minHeight = 22;

            var icon = new Label();
            icon.AddToClassList("tree-item-icon");
            icon.style.width = 16;
            icon.style.fontSize = 12;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.marginRight = 4;
            item.Add(icon);

            var label = new Label();
            label.AddToClassList("tree-item-label");
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            item.Add(label);

            var module = new Label();
            module.AddToClassList("tree-item-module");
            module.style.color = new Color(0.53f, 0.53f, 0.53f);
            module.style.fontSize = 10;
            module.style.marginRight = 8;
            item.Add(module);

            var deleteBtn = new Button { text = "×" };
            deleteBtn.AddToClassList("tree-item-delete");
            deleteBtn.style.width = 20;
            deleteBtn.style.height = 18;
            deleteBtn.style.backgroundColor = new Color(0.55f, 0.29f, 0.29f);
            deleteBtn.style.color = Color.white;
            deleteBtn.style.fontSize = 12;
            deleteBtn.style.borderTopLeftRadius = deleteBtn.style.borderTopRightRadius = 2;
            deleteBtn.style.borderBottomLeftRadius = deleteBtn.style.borderBottomRightRadius = 2;
            item.Add(deleteBtn);

            return item;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var item = _treeView.GetItemDataForIndex<TreeItemData>(index);
            if (item == null) return;

            var node = item.Node;
            bool hasError = _validationErrors.Any(e => e.Contains(node.key));

            // 图标
            var icon = element.Q<Label>(className: "tree-item-icon");
            icon.text = node.type switch
            {
                RedDotType.Normal => "●",
                RedDotType.Number => "#",
                RedDotType.New => "N",
                _ => "○"
            };
            icon.style.color = hasError ? Color.red : new Color(0.9f, 0.3f, 0.3f);

            // 标签
            var label = element.Q<Label>(className: "tree-item-label");
            label.text = node.key;
            label.style.color = hasError ? Color.red : StyleKeyword.Null;

            // 模块
            var module = element.Q<Label>(className: "tree-item-module");
            module.text = string.IsNullOrEmpty(node.module) ? "" : $"[{node.module}]";

            // 删除按钮
            var deleteBtn = element.Q<Button>(className: "tree-item-delete");
            deleteBtn.clicked += () =>
            {
                var path = GetNodePath(node.key, node.parentKey);
                if (EditorUtility.DisplayDialog("删除节点", $"确定删除节点 '{node.key}' 及其所有子节点？\n路径: {path}", "删除", "取消"))
                {
                    // 使用精确删除方法
                    _config.RemoveNode(node.key, node.parentKey);
                    // 检查选中的节点是否被删除
                    if (_selectedNode != null && _selectedNode.key == node.key && _selectedNode.parentKey == node.parentKey)
                        _selectedNode = null;
                    EditorUtility.SetDirty(_config);
                    OnConfigChanged();
                }
            };
        }

        private void OnTreeSelectionChanged(IEnumerable<object> items)
        {
            var item = items.FirstOrDefault() as TreeItemData;
            _selectedNode = item?.Node;
            RefreshDetailPanel();
        }

        private void RefreshTreeView()
        {
            if (_config == null || _treeView == null) return;

            var searchText = _searchField?.value?.ToLower() ?? "";
            var showOnlyErrors = _showOnlyErrorsToggle?.value ?? false;

            // 保存当前选中的节点 key，用于刷新后重新选中
            var selectedKey = _selectedNode?.key;

            var rootItems = new List<TreeViewItemData<TreeItemData>>();
            var roots = _config.GetRootNodes();
            int id = 0;
            _keyToIdMap.Clear(); // 清空映射

            foreach (var root in roots)
            {
                var treeItem = BuildTreeItem(root, ref id, searchText, showOnlyErrors);
                if (treeItem.HasValue)
                {
                    rootItems.Add(treeItem.Value);
                }
            }

            _treeView.SetRootItems(rootItems);
            _treeView.Rebuild();

            // 如果有选中的节点，刷新后重新选中
            if (!string.IsNullOrEmpty(selectedKey) && _keyToIdMap.TryGetValue(selectedKey, out var itemId))
            {
                _treeView.SetSelectionById(itemId);
                // 确保选中的项可见
                _treeView.ScrollToItemById(itemId);
            }
        }

        private TreeViewItemData<TreeItemData>? BuildTreeItem(RedDotNodeDefinition node, ref int id, string searchText,
            bool showOnlyErrors)
        {
            bool hasError = _validationErrors.Any(e => e.Contains(node.key));

            // 过滤
            bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                                 node.key.ToLower().Contains(searchText) ||
                                 (node.description?.ToLower().Contains(searchText) ?? false);

            if (showOnlyErrors && !hasError) return null;

            var children = _config.GetChildren(node.key);
            var childItems = new List<TreeViewItemData<TreeItemData>>();

            foreach (var child in children)
            {
                var childItem = BuildTreeItem(child, ref id, searchText, showOnlyErrors);
                if (childItem.HasValue)
                {
                    childItems.Add(childItem.Value);
                }
            }

            // 如果搜索不匹配且没有匹配的子节点，跳过
            if (!matchesSearch && childItems.Count == 0 && !string.IsNullOrEmpty(searchText))
            {
                return null;
            }

            var data = new TreeItemData { Node = node };
            var currentId = id++;
            // 建立 key 到 id 的映射
            _keyToIdMap[node.key] = currentId;
            return new TreeViewItemData<TreeItemData>(currentId, data, childItems);
        }

        private class TreeItemData
        {
            public RedDotNodeDefinition Node;
        }

        #endregion

        #region 详情面板

        private void RefreshDetailPanel()
        {
            _detailPanel.Clear();

            if (_selectedNode == null)
            {
                var hint = new Label("选择左侧节点查看详情");
                hint.AddToClassList("hint-label");
                _detailPanel.Add(hint);
                return;
            }

            var node = _selectedNode;

            // Key（可编辑）- 使用延迟更新，只在失去焦点或按 Enter 时触发
            var keyField = AddDetailRow("Key", node.key, false);
            keyField.isDelayed = true; // 延迟更新，避免每次输入都触发
            
            keyField.RegisterValueChangedCallback(evt =>
            {
                // 获取当前节点的原始 key 和 parentKey（在重命名前）
                var oldKey = node.key;
                var oldParentKey = node.parentKey;
                var newKey = evt.newValue?.Trim();

                if (string.IsNullOrEmpty(newKey))
                {
                    EditorUtility.DisplayDialog("重命名失败", "Key 不能为空", "确定");
                    keyField.SetValueWithoutNotify(oldKey);
                    return;
                }

                if (oldKey == newKey)
                {
                    return; // 没有变化
                }

                // 使用新的 RenameNode 方法（通过 key + parentKey 精确定位）
                if (_config.RenameNode(oldKey, oldParentKey, newKey))
                {
                    // 更新选中节点引用（使用新 key 和 parentKey）
                    _selectedNode = _config.GetNode(newKey, oldParentKey);
                    EditorUtility.SetDirty(_config);
                    MarkDirtyAndRefresh(); // 这会刷新树视图，并自动选中新 key 对应的节点
                    RefreshDetailPanel(); // 刷新详情面板以显示新的 Key
                }
                else
                {
                    // 计算新路径用于提示
                    var newPath = GetNodePath(newKey, oldParentKey);
                    EditorUtility.DisplayDialog("重命名失败", $"链路重复或操作失败\n新路径: {newPath}", "确定");
                    keyField.SetValueWithoutNotify(oldKey);
                }
            });

            // 父节点
            var parentField = AddDetailRow("父节点", node.parentKey, false);
            parentField.isDelayed = true; // 延迟更新，避免每次输入都触发
            parentField.RegisterValueChangedCallback(evt =>
            {
                var oldParentKey = node.parentKey;
                var newParentKey = evt.newValue?.Trim();
                
                // 检查新路径是否重复
                var tempNode = new RedDotNodeDefinition { key = node.key, parentKey = string.IsNullOrEmpty(newParentKey) ? null : newParentKey };
                var newPath = GetNodePath(node.key, tempNode.parentKey);
                
                // 检查路径是否已存在（排除当前节点自身）
                bool pathExists = false;
                foreach (var n in _config.nodes)
                {
                    if (n == node) continue; // 跳过当前节点
                    var existingPath = GetNodePath(n.key, n.parentKey);
                    if (existingPath == newPath)
                    {
                        pathExists = true;
                        break;
                    }
                }
                
                if (pathExists)
                {
                    EditorUtility.DisplayDialog("修改失败", $"链路重复\n路径: {newPath}", "确定");
                    parentField.SetValueWithoutNotify(oldParentKey);
                    return;
                }
                
                node.parentKey = string.IsNullOrEmpty(newParentKey) ? null : newParentKey;
                MarkDirtyAndRefresh();
            });

            // 类型
            var typeField = new EnumField("类型", node.type);
            typeField.RegisterValueChangedCallback(evt =>
            {
                node.type = (RedDotType)evt.newValue;
                MarkDirtyAndRefresh();
            });
            _detailPanel.Add(typeField);

            // 模块
            var moduleField = AddDetailRow("模块", node.module, false);
            moduleField.RegisterValueChangedCallback(evt =>
            {
                node.module = evt.newValue;
                MarkDirtyAndRefresh();
            });

            // 描述
            var descLabel = new Label("描述");
            _detailPanel.Add(descLabel);
            var descField = new TextField { multiline = true, value = node.description ?? "" };
            descField.style.height = 60;
            descField.RegisterValueChangedCallback(evt =>
            {
                node.description = evt.newValue;
                MarkDirtyAndRefresh();
            });
            _detailPanel.Add(descField);

            // 路径
            var pathLabel = new Label("路径");
            pathLabel.style.marginTop = 10;
            _detailPanel.Add(pathLabel);
            var path = GetNodePath(node.key);
            var pathValue = new Label(path);
            pathValue.AddToClassList("path-label");
            _detailPanel.Add(pathValue);

            // 子节点
            var children = _config.GetChildren(node.key);
            if (children.Count > 0)
            {
                var childLabel = new Label($"子节点 ({children.Count})");
                childLabel.style.marginTop = 10;
                _detailPanel.Add(childLabel);

                foreach (var child in children)
                {
                    var childBtn = new Button(() =>
                    {
                        _selectedNode = child;
                        RefreshDetailPanel();
                    });
                    childBtn.text = "  → " + child.key;
                    childBtn.AddToClassList("child-link");
                    _detailPanel.Add(childBtn);
                }
            }
        }

        private TextField AddDetailRow(string label, string value, bool readOnly)
        {
            var field = new TextField(label) { value = value ?? "" };
            if (readOnly) field.SetEnabled(false);
            _detailPanel.Add(field);
            return field;
        }

        private void RefreshValidationPanel()
        {
            _validationPanel.Clear();

            if (_validationErrors.Count > 0)
            {
                var header = new Label("验证问题");
                header.AddToClassList("section-header");
                _validationPanel.Add(header);

                foreach (var error in _validationErrors)
                {
                    var errorLabel = new Label("⚠ " + error);
                    errorLabel.AddToClassList("error-label");
                    _validationPanel.Add(errorLabel);
                }
            }
        }

        private void RefreshStatisticsPanel()
        {
            _statisticsPanel.Clear();

            if (_config == null) return;

            var header = new Label("统计信息");
            header.AddToClassList("section-header");
            _statisticsPanel.Add(header);

            _statisticsPanel.Add(new Label($"总节点数: {_config.nodes.Count}"));
            _statisticsPanel.Add(new Label($"根节点数: {_config.GetRootNodes().Count}"));

            var modules = _config.GetNodesByModule();
            _statisticsPanel.Add(new Label($"模块数: {modules.Count}"));

            var moduleHeader = new Label("按模块分布:");
            moduleHeader.style.marginTop = 5;
            _statisticsPanel.Add(moduleHeader);

            foreach (var kvp in modules.OrderByDescending(k => k.Value.Count))
            {
                var line = new Label($"  {kvp.Key}: {kvp.Value.Count} 个节点");
                line.style.fontSize = 11;
                _statisticsPanel.Add(line);
            }
        }

        #endregion

        #region 操作

        private void LoadLastConfig()
        {
            var lastConfigPath = EditorPrefs.GetString("RedDotTreeEditor_LastConfig", "");
            if (!string.IsNullOrEmpty(lastConfigPath))
            {
                _config = AssetDatabase.LoadAssetAtPath<RedDotTreeConfig>(lastConfigPath);
                if (_configField != null)
                {
                    _configField.SetValueWithoutNotify(_config);
                }
            }

            OnConfigChanged();
        }

        private void OnConfigChanged()
        {
            if (_config != null)
            {
                var path = AssetDatabase.GetAssetPath(_config);
                EditorPrefs.SetString("RedDotTreeEditor_LastConfig", path);
            }

            ValidateConfig();
            RefreshTreeView();
            RefreshDetailPanel();
            RefreshStatisticsPanel();
            UpdateStatusBar();
        }

        private void ValidateConfig()
        {
            _validationErrors = _config?.Validate() ?? new List<string>();
            RefreshValidationPanel();
            UpdateStatusBar();
        }

        private void AddNewNode()
        {
            if (_config == null) return;

            var key = _newKeyField?.value;
            if (string.IsNullOrEmpty(key)) return;

            var parentKey = _newParentField?.value;
            var module = _newModuleField?.value;
            var type = (RedDotType)(_newTypeField?.value ?? RedDotType.Normal);

            var node = new RedDotNodeDefinition
            {
                key = key,
                parentKey = string.IsNullOrEmpty(parentKey) ? null : parentKey,
                type = type,
                module = module
            };

            if (_config.AddNode(node))
            {
                EditorUtility.SetDirty(_config);
                _newKeyField.value = "";
                // 设置新创建的节点为选中节点（使用key和parentKey精确定位）
                _selectedNode = _config.GetNode(key, node.parentKey);
                OnConfigChanged(); // 这会刷新树视图并自动选中新节点
            }
            else
            {
                // 计算路径用于提示
                var path = GetNodePath(key, node.parentKey);
                EditorUtility.DisplayDialog("添加失败", $"链路重复或 Key 无效\n路径: {path}", "确定");
            }
        }

        private void MarkDirtyAndRefresh()
        {
            if (_config != null)
            {
                EditorUtility.SetDirty(_config);
            }

            ValidateConfig();
            RefreshTreeView();
        }

        private void UpdateStatusBar()
        {
            if (_statusLabel == null) return;

            if (_validationErrors.Count > 0)
            {
                _statusLabel.text = $"⚠ {_validationErrors.Count} 个问题";
                _statusLabel.style.color = Color.red;
            }
            else
            {
                _statusLabel.text = "✓ 配置有效";
                _statusLabel.style.color = Color.green;
            }

            _nodeCountLabel.text = $"节点总数: {_config?.nodes.Count ?? 0}";
        }

        private string GetNodePath(string key)
        {
            var path = new List<string>();
            var current = key;
            while (!string.IsNullOrEmpty(current))
            {
                path.Insert(0, current);
                var node = _config.GetNode(current);
                current = node?.parentKey;
            }

            return string.Join(" → ", path);
        }

        /// <summary>
        /// 通过 key 和 parentKey 计算路径
        /// </summary>
        private string GetNodePath(string key, string parentKey)
        {
            var path = new List<string>();
            var current = key;
            var currentParent = parentKey;
            var visited = new HashSet<string>(); // 防止循环依赖

            while (!string.IsNullOrEmpty(current))
            {
                if (visited.Contains(current))
                    break;
                visited.Add(current);

                path.Insert(0, current);

                if (string.IsNullOrEmpty(currentParent))
                    break;

                var parentNode = _config.GetNode(currentParent);
                if (parentNode == null)
                    break;

                current = currentParent;
                currentParent = parentNode.parentKey;
            }

            return string.Join(" → ", path);
        }

        #endregion

        #region 代码生成

        private void ExportCode()
        {
            if (_config == null) return;

            var errors = _config.Validate();
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("导出失败",
                    $"配置存在 {errors.Count} 个问题，请先修复：\n\n" + string.Join("\n", errors.Take(5)),
                    "确定");
                return;
            }

            var code = GenerateCode();
            var fullPath = Path.Combine(Application.dataPath, _config.outputPath);

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var filePath = Path.Combine(fullPath, $"{_config.className}.cs");
            File.WriteAllText(filePath, code, Encoding.UTF8);

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("导出成功",
                $"代码已导出到:\n{filePath}\n\n共 {_config.nodes.Count} 个红点 Key",
                "确定");

            var assetPath = "Assets/" + _config.outputPath + "/" + _config.className + ".cs";
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        private string GenerateCode()
        {
            var sb = new StringBuilder();
            
            // 建立节点到常量名的映射（使用路径生成唯一常量名）
            var nodeToConstMap = new Dictionary<RedDotNodeDefinition, string>();

            sb.AppendLine("// =============================================================================");
            sb.AppendLine("// 此文件由红点树编辑器自动生成，请勿手动修改");
            sb.AppendLine($"// 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"// 节点总数: {_config.nodes.Count}");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine();
            sb.AppendLine("using July.RedDot;");
            sb.AppendLine();
            sb.AppendLine($"namespace {_config.codeNamespace}");
            sb.AppendLine("{");

            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 红点 Key 定义（自动生成）");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static class {_config.className}");
            sb.AppendLine("    {");

            var modules = _config.GetNodesByModule();
            foreach (var module in modules.OrderBy(m => m.Key))
            {
                sb.AppendLine();
                sb.AppendLine($"        #region {module.Key}");
                sb.AppendLine();

                foreach (var node in module.Value.OrderBy(n => n.key))
                {
                    // 计算节点路径
                    var path = GetNodePath(node.key, node.parentKey);
                    var pathConstName = SanitizePath(path);
                    nodeToConstMap[node] = pathConstName;
                    
                    if (!string.IsNullOrEmpty(node.description))
                    {
                        sb.AppendLine($"        /// <summary>");
                        sb.AppendLine($"        /// {node.description}");
                        sb.AppendLine($"        /// 路径: {path}");
                        sb.AppendLine($"        /// </summary>");
                    }
                    else
                    {
                        sb.AppendLine($"        /// <summary>");
                        sb.AppendLine($"        /// 路径: {path}");
                        sb.AppendLine($"        /// </summary>");
                    }

                    sb.AppendLine($"        public const string {pathConstName} = \"{pathConstName}\";");
                }

                sb.AppendLine();
                sb.AppendLine($"        #endregion");
            }

            sb.AppendLine();
            sb.AppendLine("        #region Registration");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 将所有红点节点添加到构建器中（在 RedDotSystemBase 子类的 OnConfigureRedDots 中调用）");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static void RegisterAll(RedDotBuilder builder)");
            sb.AppendLine("        {");

            var roots = _config.GetRootNodes();
            foreach (var root in roots)
            {
                GenerateNodeRegistration(sb, root, "            ", nodeToConstMap);
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        #endregion");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return sb.ToString();
        }

        private void GenerateNodeRegistration(StringBuilder sb, RedDotNodeDefinition node, string indent, Dictionary<RedDotNodeDefinition, string> nodeToConstMap)
        {
            var keyConst = nodeToConstMap[node];
            string parentConst = "null";
            if (!string.IsNullOrEmpty(node.parentKey))
            {
                // 通过路径精确查找父节点
                var nodePath = GetNodePath(node.key, node.parentKey);
                var pathParts = nodePath.Split(new[] { " → " }, StringSplitOptions.None);
                if (pathParts.Length >= 2)
                {
                    // 父节点的路径是当前路径去掉最后一个部分
                    var parentPath = string.Join(" → ", pathParts.Take(pathParts.Length - 1));
                    
                    // 在所有节点中查找匹配父路径的节点
                    foreach (var kvp in nodeToConstMap)
                    {
                        var candidatePath = GetNodePath(kvp.Key.key, kvp.Key.parentKey);
                        if (candidatePath == parentPath)
                        {
                            parentConst = kvp.Value;
                            break;
                        }
                    }
                }
            }
            
            var typeStr = $"RedDotType.{node.type}";

            if (string.IsNullOrEmpty(node.parentKey))
            {
                sb.AppendLine($"{indent}builder.AddNode({keyConst}, null, {typeStr});");
            }
            else
            {
                sb.AppendLine($"{indent}builder.AddNode({keyConst}, {parentConst}, {typeStr});");
            }

            var children = _config.GetChildren(node.key);
            foreach (var child in children)
            {
                GenerateNodeRegistration(sb, child, indent, nodeToConstMap);
            }
        }

        /// <summary>
        /// 将路径字符串转换为合法的 C# 标识符
        /// </summary>
        private string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            
            // 将路径分隔符 " → " 替换为 "_"
            var sanitized = path.Replace(" → ", "_");
            // 替换其他特殊字符
            sanitized = sanitized.Replace(".", "_")
                                 .Replace("-", "_")
                                 .Replace(" ", "_")
                                 .Replace("/", "_")
                                 .Replace("\\", "_");
            
            // 确保以字母或下划线开头
            if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            {
                sanitized = "_" + sanitized;
            }
            
            return sanitized;
        }

        private string EscapeString(string str) =>
            str?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

        #endregion
    }

    // TextField 占位符扩展
    public static class TextFieldExtensions
    {
        public static void SetPlaceholderText(this TextField textField, string placeholder)
        {
            var placeholderLabel = new Label(placeholder);
            placeholderLabel.style.position = Position.Absolute;
            placeholderLabel.style.left = 3;
            placeholderLabel.style.top = 2;
            placeholderLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            placeholderLabel.pickingMode = PickingMode.Ignore;

            textField.Add(placeholderLabel);

            textField.RegisterValueChangedCallback(evt =>
            {
                placeholderLabel.style.display = string.IsNullOrEmpty(evt.newValue)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            });

            // 初始状态
            placeholderLabel.style.display = string.IsNullOrEmpty(textField.value)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }
}
