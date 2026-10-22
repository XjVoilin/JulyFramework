using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.Mesh.Authoring
{
    internal enum MeshListSort { Name, RetainedRatio, ReducedTriangles }

    public sealed class MeshAuthoringWindow : EditorWindow
    {
        MeshWorkspace workspace;
        MeshThumbnails thumbnails;
        ListView list;
        Label summary, message, count;
        Button nameSort, retainedSort, reducedSort;
        readonly List<MeshRow> displayRows = new List<MeshRow>();
        MeshListSort sort = MeshListSort.Name;
        bool descending;
        bool compact;
        readonly HashSet<RowView> views = new HashSet<RowView>();

        [MenuItem("JulyGF/资源管理/Mesh 简化")]
        public static void Open()
        {
            bool alreadyOpen = HasOpenInstances<MeshAuthoringWindow>();
            var window = GetWindow<MeshAuthoringWindow>("Mesh 简化");
            if (!alreadyOpen) window.position = new Rect(window.position.position, new Vector2(1100, 800));
        }

        void OnEnable()
        {
            workspace = new MeshWorkspace();
            workspace.Changed += Refresh;
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += SourceChanged;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            minSize = new Vector2(420, 360);
            rootVisualElement.AddToClassList("mesh-window");
            rootVisualElement.EnableInClassList("mesh-dark", EditorGUIUtility.isProSkin);
            rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.mesh.authoring/Editor/Window/MeshAuthoringWindow.uss"));
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(e => ResizeRows(e.newRect.width));
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(ReadSelection) { text = "读取所选" });
            toolbar.Add(new Label("保真") { style = { marginLeft = 8, marginRight = 5, unityTextAlign = TextAnchor.MiddleLeft } });
            var fidelity = new PopupField<string>(new List<string> { "保守", "均衡", "宽松" }, 0);
            fidelity.style.width = 80;
            fidelity.RegisterValueChangedCallback(e =>
            {
                workspace.Fidelity = fidelity.index;
                foreach (var row in workspace.Rows) workspace.Request(row, 100, true, row.Automatic);
            });
            toolbar.Add(fidelity);
            rootVisualElement.Add(toolbar);
            var sorting = new VisualElement(); sorting.AddToClassList("mesh-sortbar");
            count = new Label(); sorting.Add(count);
            nameSort = SortHeader("名称", MeshListSort.Name); sorting.Add(nameSort);
            retainedSort = SortHeader("保留比例", MeshListSort.RetainedRatio); sorting.Add(retainedSort);
            reducedSort = SortHeader("减少面数", MeshListSort.ReducedTriangles); sorting.Add(reducedSort);
            rootVisualElement.Add(sorting);
            list = new ListView { itemsSource = displayRows, fixedItemHeight = 224, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, selectionType = SelectionType.None };
            list.style.flexGrow = 1;
            list.makeItem = () => { var view = new RowView(this); views.Add(view); return view.Root; };
            list.bindItem = (element, index) => ((RowView)element.userData).Bind(displayRows[index]);
            list.unbindItem = (element, index) => ((RowView)element.userData).Release();
            list.destroyItem = element => { var view = (RowView)element.userData; view.Release(); views.Remove(view); };
            rootVisualElement.Add(list);
            message = new Label("在 Project 或 Hierarchy 选择独立 Mesh、Prefab、文件夹或场景对象，再读取所选。") { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 8, paddingRight = 8, paddingTop = 5, paddingBottom = 5 } };
            rootVisualElement.Add(message);
            var footer = new VisualElement(); footer.AddToClassList("mesh-footer");
            summary = new Label { name = "mesh-selection-summary", style = { flexGrow = 1 } }; footer.Add(summary);
            footer.Add(new Button(RestoreAllRecommendations) { text = "全部恢复推荐比例" });
            footer.Add(new Button(() => Batch(true)) { text = "还原勾选资源" });
            footer.Add(new Button(() => Batch(false)) { text = "应用勾选" });
            rootVisualElement.Add(footer);
            Refresh();
        }

        internal void ReadSelection()
        {
            try
            {
                var rows = MeshSelection.Collect(Selection.objects, out int unsupported);
                workspace.Clear(); workspace.Rows.AddRange(rows);
                foreach (var row in rows) workspace.Initialize(row);
                message.text = rows.Count == 0 ? "选择中没有可处理的独立 .asset Mesh。" : "已读取选择；比例调整只更新候选，点击应用才修改资源。";
                if (unsupported > 0) message.text += $" 已跳过 {unsupported} 个非独立 Mesh；模型内 Mesh 请先通过 Prefab 制作流程提取。";
                Refresh();
            }
            catch (Exception error) { message.text = error.Message; }
        }

        internal void RestoreAllRecommendations()
        {
            foreach (var row in workspace.Rows)
            {
                try { workspace.RestoreRecommendation(row); }
                catch (Exception error) { row.Error = error.Message; }
            }
            message.text = "列表全部项目已恢复或正在计算各自推荐比例；未修改已应用资源。";
            Refresh();
        }

        internal void Batch(bool restore)
        {
            var rows = workspace.Rows.Where(row => row.Checked).ToList();
            int completed = 0, skipped = 0, failed = 0;
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (EditorUtility.DisplayCancelableProgressBar(restore ? "还原 Mesh" : "应用 Mesh", row.Source.name, (float)i / rows.Count)) break;
                    try
                    {
                        if (row.Busy || row.Pending != null) throw new InvalidOperationException("候选正在计算，请等待完成后再操作。");
                        if (restore)
                        {
                            if (!row.CanRestore) { skipped++; continue; }
                            MeshAssets.Restore(row);
                        }
                        else
                        {
                            if (!row.Candidate || !string.IsNullOrEmpty(row.Error)) throw new InvalidOperationException(row.Error ?? "尚未生成有效候选。");
                            if (!string.IsNullOrEmpty(row.ApplyError)) throw new InvalidOperationException(row.ApplyError);
                            if (!row.HasChanges) { skipped++; continue; }
                            MeshAssets.Apply(row, workspace.Fidelity);
                        }
                        row.Error = null; completed++;
                    }
                    catch (Exception error) { row.Error = error.Message; Debug.LogError($"July Mesh：{row.Source.name} 操作失败：{error.Message}", row.Source); failed++; }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            message.text = $"完成 {completed}，跳过 {skipped}，失败 {failed}，未处理 {rows.Count - completed - skipped - failed}。";
            Refresh();
        }

        void SourceChanged()
        {
            if (workspace == null) return;
            foreach (var row in workspace.Rows) row.NeedsSourceCheck = true;
        }
        void OnProjectChange() => SourceChanged();
        internal MeshWorkspace Workspace => workspace;
        void Tick() { workspace?.Tick(); }
        internal IReadOnlyList<MeshRow> DisplayRows => displayRows;

        Button SortHeader(string title, MeshListSort column)
        {
            var header = new Button(() => SortBy(column)) { name = "mesh-sort-" + column, text = title,
                tooltip = column == MeshListSort.Name
                    ? "按 Mesh 名称自然排序，默认从低到高，再次点击切换方向；不随生成结果重排。"
                    : column == MeshListSort.RetainedRatio
                    ? "按候选实际面数保留比例排序，首次从低到高，再次点击切换方向。"
                    : "按当前面数减候选实际面数排序，首次从高到低，再次点击切换方向。" };
            header.AddToClassList("mesh-sort"); return header;
        }

        internal void SortBy(MeshListSort column)
        {
            descending = sort == column ? !descending : column == MeshListSort.ReducedTriangles;
            sort = column;
            Refresh(true);
        }

        void ResizeRows(float width)
        {
            if (list == null) return;
            bool next = width < 560;
            if (compact == next) return;
            compact = next;
            rootVisualElement.EnableInClassList("mesh-compact", compact);
            list.fixedItemHeight = compact ? 268 : 224;
            list.RefreshItems();
        }

        void Refresh() => Refresh(false);
        void Refresh(bool explicitSort)
        {
            if (list == null) return;
            var ordered = displayRows.Where(workspace.Rows.Contains).ToList();
            ordered.AddRange(workspace.Rows.Where(row => !ordered.Contains(row)));
            if (!views.Any(view => view.Editing) && (sort == MeshListSort.Name || explicitSort || !workspace.Working))
            {
                if (sort == MeshListSort.Name)
                {
                    var names = Comparer<string>.Create(EditorUtility.NaturalCompare);
                    Func<MeshRow, string> name = row => row.Source ? row.Source.name : "来源缺失";
                    ordered = (descending ? ordered.OrderByDescending(name, names) : ordered.OrderBy(name, names)).ToList();
                }
                else
                {
                    // Missing candidates remain after computed rows in either direction.
                    Func<MeshRow, double> key = row => sort == MeshListSort.RetainedRatio ? row.ActualRatio : row.CurrentTriangles - row.CandidateTriangles;
                    var ready = ordered.OrderBy(row => !row.Candidate);
                    ordered = (descending ? ready.ThenByDescending(key) : ready.ThenBy(key)).ToList();
                }
            }
            bool changed = !displayRows.SequenceEqual(ordered);
            if (changed) { displayRows.Clear(); displayRows.AddRange(ordered); list.Rebuild(); }
            else foreach (var view in views) view.Refresh();
            count.text = $"{workspace.Rows.Count} 个 Mesh";
            var selected = workspace.Rows.Where(row => row.Checked).ToList();
            var eligible = selected.Where(row => row.Candidate && !row.Busy && row.Pending == null
                && string.IsNullOrEmpty(row.Error) && string.IsNullOrEmpty(row.ApplyError)).ToList();
            long reduced = eligible.Sum(row => (long)row.CurrentTriangles - row.CandidateTriangles);
            int excluded = selected.Count - eligible.Count;
            summary.text = $"已勾选 {selected.Count} / {workspace.Rows.Count} 项 · 预计{(reduced >= 0 ? "减少" : "增加")} {Math.Abs(reduced):N0} 面"
                + (excluded == 0 ? "" : $"（{excluded} 项未计入）");
            summary.tooltip = "按勾选项的当前面数减待应用实际面数求和，每项 Mesh 计一次，不乘场景实例数。计算中、无候选或存在错误的项目暂不计入；完成计算后自动更新。";
            nameSort.text = "名称" + (sort == MeshListSort.Name ? descending ? "  ▼" : "  ▲" : "");
            retainedSort.text = "保留比例" + (sort == MeshListSort.RetainedRatio ? descending ? "  ▼" : "  ▲" : "");
            reducedSort.text = "减少面数" + (sort == MeshListSort.ReducedTriangles ? descending ? "  ▼" : "  ▲" : "");
            nameSort.EnableInClassList("sorted", sort == MeshListSort.Name);
            retainedSort.EnableInClassList("sorted", sort == MeshListSort.RetainedRatio);
            reducedSort.EnableInClassList("sorted", sort == MeshListSort.ReducedTriangles);
        }
        void OnDisable()
        {
            EditorApplication.update -= Tick; Undo.undoRedoPerformed -= SourceChanged;
            list = null;
            if (workspace != null) { workspace.Changed -= Refresh; workspace.Dispose(); workspace = null; }
            foreach (var view in views) view.Release(); views.Clear();
            thumbnails?.Dispose(); thumbnails = null;
            EditorUtility.ClearProgressBar();
        }

        sealed class RowView
        {
            internal readonly VisualElement Root;
            readonly MeshAuthoringWindow window;
            readonly Toggle check;
            readonly Label name, source, saved, originalCount, candidateCount, state, actual;
            readonly FloatField ratio;
            readonly Slider slider;
            readonly Image originalImage, candidateImage;
            MeshRow row;
            UnityEngine.Mesh renderedOriginal, renderedCandidate;
            Texture2D originalTexture, candidateTexture;
            bool focused, dragging;
            internal bool Editing => focused || dragging;

            internal RowView(MeshAuthoringWindow window)
            {
                this.window = window;
                Root = new VisualElement { userData = this };
                Root.AddToClassList("mesh-item");
                var header = Element("mesh-item-header"); Root.Add(header);
                check = new Toggle { name = "mesh-check" };
                check.RegisterValueChangedCallback(e => { row.Checked = e.newValue; window.Refresh(); }); header.Add(check);
                name = new Label { name = "mesh-name", tooltip = "单击定位来源 Mesh 资源" };
                name.AddToClassList("mesh-name"); header.Add(name);
                source = new Label(); source.AddToClassList("mesh-source"); header.Add(source);
                saved = new Label(); saved.AddToClassList("mesh-saved"); header.Add(saved);
                var line = Element("mesh-content"); Root.Add(line);
                var left = PreviewColumn("当前", out originalImage, out originalCount); line.Add(left);
                var right = PreviewColumn("调整后", out candidateImage, out candidateCount); line.Add(right);
                name.RegisterCallback<ClickEvent>(Locate);
                originalImage.RegisterCallback<ClickEvent>(Locate);
                candidateImage.RegisterCallback<ClickEvent>(Locate);
                var controls = Element("mesh-controls"); line.Add(controls);
                var ratioLine = Element("mesh-ratio-line"); controls.Add(ratioLine);
                ratioLine.Add(new Label("保留") { tooltip = "相对本次基准 Mesh 的目标面数比例；手动调整解除误差限制。" });
                ratio = new FloatField { name = "mesh-ratio", isDelayed = false };
                ratio.RegisterValueChangedCallback(e =>
                {
                    if (!float.IsNaN(e.newValue) && e.newValue > 0 && e.newValue <= 100) window.workspace.Request(row, e.newValue, false);
                    else
                    {
                        row.Revision++; row.Pending = null; row.Busy = false;
                        row.Error = "保留比例必须大于 0 且不超过 100%。";
                        state.text = row.Error;
                    }
                });
                ratio.RegisterCallback<FocusInEvent>(e => focused = true);
                ratio.RegisterCallback<FocusOutEvent>(e => { focused = false; window.rootVisualElement.schedule.Execute(window.Refresh); });
                ratioLine.Add(ratio); ratioLine.Add(new Label("%"));
                slider = new Slider(1, 100) { name = "mesh-slider" };
                slider.RegisterValueChangedCallback(e => window.workspace.Request(row, e.newValue, false));
                slider.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) dragging = true; }, TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerUpEvent>(e => EndDrag(), TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerCaptureOutEvent>(e => EndDrag());
                slider.RegisterCallback<PointerCancelEvent>(e => EndDrag());
                controls.Add(slider);
                actual = new Label(); actual.AddToClassList("mesh-actual"); controls.Add(actual);
                controls.Add(new Button(() =>
                {
                    try { window.workspace.RestoreRecommendation(row); }
                    catch (Exception error) { row.Error = error.Message; window.Refresh(); }
                }) { text = "恢复推荐" });
                state = new Label(); state.AddToClassList("mesh-state"); Root.Add(state);
            }

            void EndDrag()
            {
                dragging = false;
                window.rootVisualElement.schedule.Execute(window.Refresh);
            }

            void Locate(ClickEvent e)
            {
                if (e.button != 0 || !row.Source) return;
                Selection.activeObject = row.Source;
                EditorGUIUtility.PingObject(row.Source);
            }

            static VisualElement Element(string className)
            {
                var element = new VisualElement(); element.AddToClassList(className); return element;
            }

            static VisualElement PreviewColumn(string title, out Image image, out Label count)
            {
                var column = Element("mesh-preview-column");
                image = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = "单击定位来源 Mesh 资源" };
                image.AddToClassList("mesh-thumbnail"); column.Add(image);
                var caption = Element("mesh-caption"); column.Add(caption);
                caption.Add(new Label(title)); count = new Label(); caption.Add(count);
                return column;
            }

            internal void Bind(MeshRow value)
            {
                if (row != value) Release();
                row = value;
                check.SetValueWithoutNotify(row.Checked);
                name.text = row.Source ? row.Source.name : "来源缺失";
                string path = AssetDatabase.GetAssetPath(row.Source);
                source.text = System.IO.Path.GetFileName(path);
                source.tooltip = path + "\n" + row.ScopeLabel + "\n独立 Mesh 原位修改，Library 缓存基准";
                int delta = row.CurrentTriangles - row.CandidateTriangles;
                saved.text = row.Candidate ? $"{(delta >= 0 ? "减少" : "增加")} {Math.Abs(delta):N0} 面" : "减少 — 面";
                originalCount.text = row.Geometry == null ? "等待分析" : row.CurrentTriangles.ToString("N0") + " 面";
                candidateCount.text = row.Candidate ? row.CandidateTriangles.ToString("N0") + " 面" : "尚无候选";
                if (!focused) ratio.SetValueWithoutNotify(row.Ratio);
                slider.SetValueWithoutNotify(row.Ratio);
                actual.text = row.Recommendation == null ? "推荐待计算" : $"推荐 {row.RecommendedRatio:0.##}%";
                actual.tooltip = $"基准 {row.OriginalTriangles:N0} 面；所有保留比例相对该基准。";
                if (row.Candidate && !row.Automatic && Mathf.Abs(row.ActualRatio - row.Ratio) > .1f) actual.text += $" · 实际 {row.ActualRatio:0.##}%";
                state.text = row.Error ?? row.ApplyError ?? (row.Busy ? "计算中；显示上次完成的候选。" : row.HasChanges ? "有待应用的调整 · 影响该资源全部引用" : row.HasApplied ? $"当前结果已应用 · 保留 {row.AppliedRatio:0.##}%" : "当前资源未修改。");
                state.tooltip = state.text;
                if (row.Geometry == null) return;
                if (window.thumbnails == null) window.thumbnails = new MeshThumbnails();
                if (renderedOriginal != row.CurrentPreview && row.CurrentPreview)
                {
                    if (originalTexture) DestroyImmediate(originalTexture);
                    originalTexture = window.thumbnails.Render(row.CurrentPreview, row.Geometry.Bounds);
                    renderedOriginal = row.CurrentPreview; originalImage.image = originalTexture;
                }
                if (renderedCandidate != row.Candidate)
                {
                    if (candidateTexture) DestroyImmediate(candidateTexture);
                    candidateTexture = row.Candidate ? window.thumbnails.Render(row.Candidate, row.Geometry.Bounds) : null;
                    renderedCandidate = row.Candidate; candidateImage.image = candidateTexture;
                }
            }

            internal void Refresh() { if (row != null) Bind(row); }

            internal void Release()
            {
                row = null; renderedOriginal = null; renderedCandidate = null; focused = false; dragging = false;
                if (originalTexture) DestroyImmediate(originalTexture); if (candidateTexture) DestroyImmediate(candidateTexture);
                originalTexture = null; candidateTexture = null;
                originalImage.image = null; candidateImage.image = null;
            }
        }
    }
}
