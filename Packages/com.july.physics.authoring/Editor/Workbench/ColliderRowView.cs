using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    // Owns only visible-row graphics. Inspection facts and persistent decisions belong to the workspace.
    internal sealed class ColliderRowView
    {
        internal readonly VisualElement Root;
        internal bool Dirty;
        readonly ColliderOverviewWindow window;
        readonly Toggle selected;
        readonly Label name, badge, state, origin;
        readonly Label[] counts, details, geometry, compositions;
        readonly Button[] performance;
        readonly Image[] images;
        readonly Texture2D[] textures;
        readonly ColliderPreviewShapes[] meshes;
        readonly string[] currentStamps;
        readonly ColliderGeneration.Preview[] rendered;
        readonly ProgressBar[] bars;
        readonly ToolbarMenu generate, apply;
        readonly Button[] reuse, reuseCurrent;
        readonly Button generateAction, applyAction, reviewAction;
        readonly Button[] retry, tune;
        ColliderRow row;
        ColliderPreview.Model model;
        Vector2 drag;

        internal ColliderRowView(ColliderOverviewWindow window)
        {
            this.window = window;
            int count = window.Workspace.Profile.Targets.Length; int columns = 1 + count * 2;
            counts = new Label[columns]; details = new Label[columns]; images = new Image[columns]; textures = new Texture2D[columns];
            geometry = new Label[columns];
            compositions=new Label[columns];performance=new Button[columns];
            meshes = new ColliderPreviewShapes[count * 2]; currentStamps = new string[count]; rendered = new ColliderGeneration.Preview[count]; bars = new ProgressBar[count]; reuse = new Button[count]; reuseCurrent = new Button[count]; retry = new Button[count]; tune = new Button[count];
            Root = new VisualElement { userData = this }; Root.AddToClassList("collider-item");
            var header = ColliderOverviewWindow.Line();
            header.AddToClassList("collider-item-header");
            selected = new Toggle(); selected.RegisterValueChangedCallback(e => { row.Selected = e.newValue; window.Refresh(); }); header.Add(selected);
            var title = new VisualElement(); title.AddToClassList("collider-item-title"); header.Add(title);
            name = new Label(); name.AddToClassList("collider-name"); title.Add(name);
            badge = new Label { name = "collider-item-badge" }; badge.AddToClassList("collider-item-badge"); title.Add(badge);
            name.RegisterCallback<ClickEvent>(e => { if (e.button == 0) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(row.Path)); });
            origin = new Label(); origin.AddToClassList("collider-secondary"); header.Add(origin);
            Root.Add(header);
            state = new Label(); state.AddToClassList("collider-row-state"); Root.Add(state);
            var body = new VisualElement(); Root.Add(body);
            var previews = new VisualElement(); previews.AddToClassList("collider-five"); body.Add(previews);
            var reference = new VisualElement(); reference.AddToClassList("collider-reference"); previews.Add(reference);
            Column(reference, 0);
            for (int goal = 0; goal < window.Workspace.Profile.Targets.Length; goal++)
            {
                var group = new VisualElement(); group.AddToClassList("collider-goal"); previews.Add(group);
                var pair = new VisualElement(); pair.AddToClassList("collider-pair"); group.Add(pair);
                var current = new VisualElement(); current.AddToClassList("collider-preview"); current.AddToClassList("collider-current"); pair.Add(current); Column(current, 1 + goal * 2);
                var candidate = new VisualElement(); candidate.AddToClassList("collider-preview"); candidate.AddToClassList("collider-candidate"); pair.Add(candidate); Column(candidate, 2 + goal * 2);
            }
            var controls = ColliderOverviewWindow.Line(); controls.AddToClassList("collider-item-actions"); Root.Insert(1,controls);
            var generateGroup = new VisualElement(); generateGroup.AddToClassList("collider-split-action"); controls.Add(generateGroup);
            generateGroup.EnableInClassList("collider-scoped-action", count > 1);
            generate = new ToolbarMenu { name = "collider-row-generate" }; generateGroup.Add(generate);
            generate.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            generateAction = new Button(() => window.Generate(new[] { row }, row.GenerateScope))
            { name = "collider-row-generate-action", text = "生成预览", tooltip = "按所选范围生成当前Prefab的候选，不修改Prefab。" }; generateGroup.Add(generateAction);
            var applyGroup = new VisualElement(); applyGroup.AddToClassList("collider-split-action"); controls.Add(applyGroup);
            applyGroup.EnableInClassList("collider-scoped-action", count > 1);
            apply = new ToolbarMenu { name = "collider-row-apply" }; applyGroup.Add(apply);
            apply.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            applyAction = new Button(() => window.Run(() => window.Workspace.Apply(new[] { row }, row.ApplyScope)))
            { name = "collider-row-apply-action", text = "应用候选", tooltip = "按所选范围应用当前Prefab的候选。" }; applyAction.AddToClassList("collider-apply"); applyGroup.Add(applyAction);
            reviewAction = new Button(() => window.Run(() => { if (row.IsConfirmed) window.Workspace.MarkPending(row); else window.Workspace.MarkHandled(row); }))
                { name = "collider-row-review-action" };
            reviewAction.AddToClassList("collider-review-action"); controls.Add(reviewAction);
        }

        static bool CanApply(ColliderRow item, ColliderTargetSelection scope) => item.Owner.Indices(scope).All(item.CanApply);

        void BindActionMenus(ColliderRow item)
        {
            for (int i = 0; i < window.ScopeOptions.Length; i++)
            {
                var scope = window.ScopeOptions[i];
                // A menu choice changes only the captured item's preference; buttons execute.
                generate.menu.AppendAction(window.RowScopeName(scope), _ => { item.GenerateScope = scope; if (row == item) Refresh(); },
                    _ => !Editable() ? DropdownMenuAction.Status.Disabled : item.GenerateScope == scope ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                apply.menu.AppendAction(window.RowScopeName(scope), _ => { item.ApplyScope = scope; if (row == item) Refresh(); },
                    _ => !Editable() ? DropdownMenuAction.Status.Disabled : item.ApplyScope == scope ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            bool Editable() => !window.Workspace.Busy && !EditorApplication.isPlayingOrWillChangePlaymode;
        }
        void Column(VisualElement parent, int index)
        {
            if (index > 0)
            {
                counts[index] = new Label { name = "collider-count-" + index }; counts[index].AddToClassList("collider-count");
                counts[index].RegisterCallback<ClickEvent>(e =>
                {
                    if (e.button != 0) return;
                    EditorUtility.DisplayDialog("碰撞体组成", counts[index].tooltip ?? "尚无结果", "关闭");
                });
            }
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, name = "collider-image-" + index }; images[index] = image;
            image.AddToClassList("collider-image"); parent.Add(image);
            if (index > 0 && index % 2 == 0)
            {
                int goal = index / 2 - 1;
                var actions = new VisualElement(); actions.AddToClassList("collider-count-row"); parent.Add(actions); actions.Add(counts[index]);
                tune[goal] = new Button(() => ColliderItemSettingsWindow.Open(window, row, goal)) { name = "collider-tune-" + goal, text = "调参" };
                tune[goal].AddToClassList("collider-tune"); actions.Add(tune[goal]);
            }
            else if (index > 0) parent.Add(counts[index]);
            else parent.Add(new VisualElement { style = { height = 22, flexShrink = 0 } });
            geometry[index] = new Label { name = "collider-geometry-" + index, tooltip = ColliderStatistics.GeometryNote };
            geometry[index].AddToClassList("collider-geometry"); parent.Add(geometry[index]);
            if(index>0)
            {
                compositions[index]=new Label();compositions[index].AddToClassList("collider-composition");parent.Insert(parent.IndexOf(geometry[index]),compositions[index]);
                performance[index]=new Button(()=>EditorUtility.DisplayDialog("碰撞成本提示",performance[index].tooltip,"关闭")){name="collider-performance-"+index};
                performance[index].AddToClassList("collider-performance-badge");parent.Add(performance[index]);
            }
            image.RegisterCallback<GeometryChangedEvent>(e => Dirty = true);
            image.RegisterCallback<PointerDownEvent>(e =>
            { if (e.button != 0) return; drag = e.position; image.CapturePointer(e.pointerId); e.StopPropagation(); });
            image.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!image.HasPointerCapture(e.pointerId)) return;
                Vector2 position = e.position; var delta = position - drag;
                row.Rotation += new Vector2(delta.y, -delta.x) * .5f; row.Rotation.x = Mathf.Clamp(row.Rotation.x, -89, 89);
                drag = position; Dirty = true; e.StopPropagation();
            });
            image.RegisterCallback<PointerUpEvent>(e => image.ReleasePointer(e.pointerId));
            if (index > 0 && index % 2 == 0)
            {
                int goal = index / 2 - 1;
                reuse[goal] = new Button(() => window.Run(() => window.Workspace.ReuseEnvelopePreview(row,
                    row.Profile.Targets.First(t => t.Settings.Goal == ColliderGeometryGoal.Envelope).Id, row.Profile.Targets[goal].Id)))
                { name = "collider-reuse-envelope-" + goal, text = "复用整体候选",
                    tooltip = "点击后原样复制整体包围候选，替换本栏候选并暂存；不重新计算，不修改Prefab。" };
                reuse[goal].AddToClassList("collider-reuse"); parent.Add(reuse[goal]);
                reuseCurrent[goal] = new Button(() => window.Run(() => window.Workspace.ReuseCurrentEnvelope(row,row.Profile.Targets[goal].Id)))
                    { name = "collider-reuse-current-" + goal, text = "复用当前" };
                reuseCurrent[goal].AddToClassList("collider-reuse"); parent.Add(reuseCurrent[goal]);
                bars[goal] = new ProgressBar { title = "尚未生成", lowValue = 0, highValue = 100 };
                bars[goal].AddToClassList("collider-progress"); parent.Add(bars[goal]);
                retry[goal] = new Button(() => window.Generate(new[] { row }, ColliderTargetSelection.Only(window.Workspace.Profile.Targets[goal].Id)))
                    { name = "collider-retry-" + goal, text = "重新生成本栏" }; retry[goal].AddToClassList("collider-retry"); parent.Add(retry[goal]);
            }
            if (index > 0)
            {
                details[index] = new Label(); details[index].AddToClassList(index % 2 == 0 ? "collider-stage" : "collider-current-detail"); parent.Add(details[index]);
            }
        }

        internal void Bind(ColliderRow value)
        {
            Release(); row = value; Dirty = true; name.text = row.Name; name.tooltip = row.Path;
            BindActionMenus(value);
            Refresh();
        }

        internal void Refresh()
        {
            if (row == null) return;
            var layout = row.Inspection.Layout;
            badge.text = layout.Badge;
            badge.tooltip = layout.BadgeTooltip;
            badge.style.display = string.IsNullOrEmpty(layout.Badge) ? DisplayStyle.None : DisplayStyle.Flex;
            badge.EnableInClassList("collider-badge-warning", layout.BadgeWarning);
            geometry[0].text = row.Inspection.Ready ? row.Inspection.ModelGeometry.ColumnText : "顶点 —\n三角面 —";
            selected.SetValueWithoutNotify(row.Selected);
            origin.text = row.Review == null ? "登记失败" : "新";
            origin.style.display = row.Review == null || row.Review.isNew ? DisplayStyle.Flex : DisplayStyle.None;
            origin.tooltip = row.Review == null ? "重新读取以完成登记" : "新纳入且尚未处理；检查后点击“标记完成”，保留当前结果。";
            Root.EnableInClassList("collider-item-selected", row.Selected);
            var errors = row.Errors.Distinct().ToArray();
            bool recheck = Enumerable.Range(0, row.Profile.Targets.Length).Any(row.NeedsReview);
            var summary = string.Join("　｜　", Enumerable.Range(0, row.Profile.Targets.Length).Select(i => row.Profile.Targets[i].Name + "：" + Status(i)));
            state.text = errors.Length == 0 ? summary : errors[0];
            state.style.display = errors.Length > 0 || recheck ? DisplayStyle.Flex : DisplayStyle.None;
            state.tooltip = string.Join("\n", new[] { summary }.Concat(errors));
            state.EnableInClassList("collider-error", errors.Length > 0);
            bool editable = !window.Workspace.Busy && !EditorApplication.isPlayingOrWillChangePlaymode;
            generate.SetEnabled(editable); apply.SetEnabled(editable);
            generate.text = window.RowScopeName(row.GenerateScope);
            apply.text = window.RowScopeName(row.ApplyScope);
            generate.tooltip = "预览范围：" + window.ScopeName(row.GenerateScope) + "；选择后不执行，点击右侧生成预览。";
            apply.tooltip = "应用范围：" + window.ScopeName(row.ApplyScope) + "；选择后不执行，点击右侧应用候选。";
            generateAction.SetEnabled(editable && row.Inspection.Ready);
            applyAction.SetEnabled(editable && CanApply(row, row.ApplyScope));
            reviewAction.text = row.IsConfirmed ? "标为待处理" : "标记完成";
            reviewAction.tooltip = row.IsConfirmed ? "将此项移回待处理，仅修改审阅状态；不撤销已应用的碰撞体，候选保持。" : "将当前所有输出组标记为审阅完成，移入已完成；不应用候选。";
            reviewAction.SetEnabled(editable && row.Review != null && (row.IsConfirmed || row.Inspection.Ready && row.Inspection.Current.All(c => c.Count > 0 && c.Issue == null)));
            for (int i = 0; i < rendered.Length; i++)
            {
                var current = row.Inspection.Current[i]; var candidate = row.Candidates[i];
                var settings = row.EffectiveSettings(i);
                var source = row.Profile.Targets.FirstOrDefault(t => t.Settings.Goal == ColliderGeometryGoal.Envelope);
                bool canShowReuse = settings.Goal == ColliderGeometryGoal.Structure && source != null;
                reuse[i].style.display = canShowReuse ? DisplayStyle.Flex : DisplayStyle.None;
                reuse[i].SetEnabled(editable && canShowReuse &&
                    window.Workspace.CanReuseEnvelopePreview(row, source.Id, row.Profile.Targets[i].Id));
                reuse[i].tooltip = "点击后原样复制整体包围候选并替换本栏候选，不修改Prefab。" +
                    (canShowReuse ? "\n来源：" + source.Name + "；" +
                        (window.Workspace.ReuseEnvelopeIssue(row, Array.IndexOf(row.Profile.Targets,source),i) ?? "可复用") : "");
                bool isEnvelope = settings.Goal == ColliderGeometryGoal.Envelope;
                reuseCurrent[i].style.display = isEnvelope ? DisplayStyle.Flex : DisplayStyle.None;
                reuseCurrent[i].SetEnabled(editable && isEnvelope && window.Workspace.CanReuseCurrentEnvelope(row,row.Profile.Targets[i].Id));
                reuseCurrent[i].tooltip = "将本栏当前的盒、球、胶囊原样复制为候选并暂存，不重新拟合，不修改Prefab。\n" +
                    (isEnvelope ? window.Workspace.CurrentEnvelopeIssue(row,i) ?? "可复用；保留当前数量，生成数量参数仅在重新生成时生效。" : "");
                tune[i].SetEnabled(editable && row.Review != null);
                tune[i].text = row.Recipe(i) == null ? "调参" : "调参*";
                tune[i].tooltip = $"仅此Prefab · {row.Profile.Targets[i].Name}\n" +
                    (settings.Goal == ColliderGeometryGoal.Structure ? $"碰撞体数≤{settings.TargetCount} · 单凸包顶点数≤{settings.MaxHullVertices} · 碰撞网格总顶点数预算≤{settings.TargetCount * settings.MaxHullVertices}\n凸包额外内缩≤{settings.PickingInwardErrorPercent:0.##}% · 原生形状偏差≤{settings.NativeShapeErrorPercent:0.##}% · 点击范围变化≤{settings.NativeClickChangePercent:0.##}%" : $"碰撞体数≤{settings.TargetCount} · 允许轮廓误差 {settings.SilhouetteErrorPercent:0.##}%") +
                    "\n" + (row.Recipe(i) == null ? "继承默认参数" : "使用独立参数");
                int col = 1 + i * 2;
                var currentCounts = ColliderStatistics.Current(row, i);
                counts[col].text = row.Inspection.Ready ? currentCounts.Total>0?$"{currentCounts.Total} 个碰撞体":"未配置" : "未读取";
                geometry[col].text = row.Inspection.Ready ? GeometryText(currentCounts,current.Geometry) : "";
                geometry[col + 1].text = candidate.Preview == null ? "" : GeometryText(ColliderStatistics.Candidate(candidate.Preview),ColliderGeometryCounts.Candidate(candidate.Preview));
                counts[col].tooltip = counts[col].text + $" · {current.KindCount}种\n" + string.Join("\n", new[] { current.Composition, currentCounts.Note, current.PreviewNote, current.Issue, current.Notice, current.Details }.Where(t => !string.IsNullOrEmpty(t)));
                details[col].text = string.Join("\n", new[] { current.Issue, current.Notice }.Where(t => !string.IsNullOrEmpty(t)));
                details[col].tooltip = counts[col].tooltip;
                details[col].style.display = details[col].text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                counts[col + 1].text = candidate.Preview == null ? "待生成" : $"{candidate.Preview.Count} 个碰撞体";
                counts[col + 1].tooltip = candidate.Preview == null ? "未生成" :
                    counts[col + 1].text + $" · {candidate.Preview.KindCount}种\n" + candidate.Preview.Composition + "\n" + candidate.Preview.Summary +
                    (row.IsCurrent(i) ? "" : "\n已过期");
                compositions[col].text=CompositionText(currentCounts);
                compositions[col+1].text=candidate.Preview==null?"":CompositionText(ColliderStatistics.Candidate(candidate.Preview));
                SetPerformance(col,window.Performance(row,i,ColliderData.Current));
                SetPerformance(col+1,window.Performance(row,i,ColliderData.Candidate));
                if (currentStamps[i] != current.Stamp || rendered[i] != candidate.Preview) Dirty = true;
            }
            UpdateProgress();
        }

        static string GeometryText(ColliderCounts counts,ColliderGeometryCounts value) => counts.Total==0?"":counts.Convex+counts.Outside==0?"无碰撞网格":$"顶点 {value.Vertices:N0} · 三角面 {value.Triangles:N0}";
        static string CompositionText(ColliderCounts counts) => counts.Total==0?"":string.Join(" · ",new[]{counts.Native>0?$"原生 {counts.Native}":null,counts.Convex>0?$"凸包 {counts.Convex}":null,counts.Outside>0?$"其它 {counts.Outside}":null}.Where(s=>s!=null));
        void SetPerformance(int index,ColliderPerformanceReport report)
        {
            var badge=performance[index];badge.text=report.Summary;badge.tooltip=report.Details;
            badge.style.display=report.Exists?DisplayStyle.Flex:DisplayStyle.None;
            badge.EnableInClassList("collider-risk-reminder",report.Level==ColliderPerformanceLevel.Reminder);
            badge.EnableInClassList("collider-risk-attention",report.Level==ColliderPerformanceLevel.Attention);
            badge.EnableInClassList("collider-risk-rule",report.Level==ColliderPerformanceLevel.RuleMismatch);
        }

        string Status(int index)
        {
            var candidate = row.Candidates[index];
            string operation = candidate.Progress switch
            {
                ColliderProgress.Queued => "排队中",
                ColliderProgress.Running => $"计算中 {(int)(candidate.Fraction * 100)}%",
                ColliderProgress.Failed => "生成失败",
                ColliderProgress.Cancelled => "已取消",
                ColliderProgress.Complete when candidate.Timer.ElapsedTicks > 0 && !candidate.Applied => "预览完成 100%",
                _ => null
            };
            string value = operation ?? (row.NeedsReview(index) ? "需复审" : row.Decision(index) switch
            {
                ColliderDecision.Keep => "已检查",
                ColliderDecision.Manual => "需手调",
                _ => "待审阅"
            });
            if (operation != null && row.NeedsReview(index)) value += " · 需复审";
            if (row.Inspection.Current[index].Count == 0) value += " · 缺失";
            if (row.Inspection.Current[index].Issue != null || !row.Inspection.Ready) value += " · 异常";
            return value;
        }

        internal void UpdateProgress()
        {
            if (row == null) return;
            for (int i = 0; i < rendered.Length; i++)
            {
                var candidate = row.Candidates[i];
                string status = candidate.Progress switch
                {
                    ColliderProgress.Queued => "排队中", ColliderProgress.Running => "计算中",
                    ColliderProgress.Complete => "已生成", ColliderProgress.Cancelled => "已取消",
                    ColliderProgress.Failed => "失败", _ => "尚未生成"
                };
                bool complete = candidate.Progress == ColliderProgress.Complete;
                bars[i].EnableInClassList("collider-progress-complete", complete);
                bars[i].value = candidate.Fraction * 100;
                bars[i].title = (complete ? "✓ 已生成" : $"{status} {(int)(candidate.Fraction * 100)}%") +
                    (candidate.Timer.ElapsedTicks > 0 ? $" · {candidate.Timer.Elapsed.TotalSeconds:F1}s" : "");
                var stage = details[2 + i * 2];
                bool expired = candidate.Preview != null && !row.IsCurrent(i);
                bool generating = candidate.Progress == ColliderProgress.Queued || candidate.Progress == ColliderProgress.Running;
                bool feedback = candidate.Timer.ElapsedTicks > 0 || generating ||
                    candidate.Progress == ColliderProgress.Failed || candidate.Progress == ColliderProgress.Cancelled;
                bars[i].style.display = generating ? DisplayStyle.Flex : DisplayStyle.None;
                stage.style.display = feedback || expired || candidate.Preview!=null ? DisplayStyle.Flex : DisplayStyle.None;
                retry[i].style.display = candidate.Progress == ColliderProgress.Failed ? DisplayStyle.Flex : DisplayStyle.None;
                retry[i].SetEnabled(!window.Workspace.Busy && !EditorApplication.isPlayingOrWillChangePlaymode && row.Inspection.Ready);
                bool interrupted = candidate.Progress == ColliderProgress.Failed || candidate.Progress == ColliderProgress.Cancelled;
                stage.text = generating || interrupted ? candidate.Stage : expired ?
                    candidate.Preview.Goal==ColliderGeometryGoal.Envelope&&candidate.Preview.NativeCount!=candidate.Preview.Count?"旧版网格候选，请重新生成":"候选已过期，请重新生成" :
                    candidate.Applied?"已应用":candidate.Preview!=null?"已生成"+(candidate.Timer.ElapsedTicks>0?$" · {candidate.Timer.Elapsed.TotalSeconds:F1}s":""):"";
                if (row.NeedsReview(i)) { stage.text += " · 当前需复审"; stage.style.display = DisplayStyle.Flex; }
                if (candidate.Preview != null && interrupted)
                    stage.text += expired ? "（保留上次成功候选，已过期）" : "（保留上次成功候选）";
                stage.tooltip = stage.text + "\n上次成功：" + candidate.SavedAt + "\n" + candidate.Error + "\n" + candidate.Preview?.Summary;
            }
        }

        internal void RenderIfNeeded()
        {
            if (!Dirty || row == null || Root.panel == null || images[0].resolvedStyle.width <= 1 || !window.IsVisible(Root)) return;
            Dirty = false;
            try
            {
                if (model == null) model = ColliderPreview.Model.Read(row);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(row.Path);
                for (int i = 0; i < rendered.Length; i++)
                {
                    if (currentStamps[i] != row.Inspection.Current[i].Stamp)
                    {
                        meshes[i * 2]?.Dispose(); meshes[i * 2] = null;
                        currentStamps[i] = row.Inspection.Current[i].Stamp;
                        try { if (row.Inspection.Current[i].Count > 0) meshes[i * 2] = new ColliderPreviewShapes(ColliderGeneration.CreateCurrentPreviewMesh(row.Inspection.Current[i].Shapes, asset.transform.worldToLocalMatrix, false)); }
                        catch (Exception error) when (error is NotSupportedException || error is ArgumentException)
                        { ShowRenderError(error); }
                    }
                    if (rendered[i] != row.Candidates[i].Preview)
                    {
                        meshes[i * 2 + 1]?.Dispose(); meshes[i * 2 + 1] = null;
                        rendered[i] = row.Candidates[i].Preview;
                        if (rendered[i] != null) meshes[i * 2 + 1] = new ColliderPreviewShapes(ColliderGeneration.CreatePreviewMesh(rendered[i], row.Candidates[i].WorldToItem));
                    }
                }
                int width = images.Min(image => Mathf.Max(1, Mathf.RoundToInt(image.resolvedStyle.width)));
                var bounds = model.Bounds;
                foreach (var mesh in meshes) if (mesh != null && mesh.Surface.vertexCount > 0) bounds.Encapsulate(mesh.Surface.bounds);
                for (int i = 0; i < images.Length; i++)
                {
                    images[i].image = null;
                    Object.DestroyImmediate(textures[i]); textures[i] = null;
                    if (i == 0 || meshes[i - 1] != null)
                        textures[i] = window.Render(model, row, i == 0 ? null : meshes[i - 1], i == 0 ? 0 : i % 2 == 1 ? 1 : 2, width, bounds);
                    images[i].image = textures[i];
                }
            }
            catch (Exception error) { ShowRenderError(error); }
        }

        void ShowRenderError(Exception error)
        {
            row.Error = error.Message;
            Refresh();
            state.tooltip = error.ToString();
            // Refresh queue counts after the active visible-row render loop has finished.
            Root.schedule.Execute(window.Refresh);
        }

        internal void Release()
        {
            foreach (var image in images) image.image = null;
            for (int i = 0; i < textures.Length; i++) { Object.DestroyImmediate(textures[i]); textures[i] = null; }
            for (int i = 0; i < meshes.Length; i++) { meshes[i]?.Dispose(); meshes[i] = null; }
            for (int i = 0; i < rendered.Length; i++) { currentStamps[i] = null; rendered[i] = null; }
            generate.menu.MenuItems().Clear(); apply.menu.MenuItems().Clear();
            row = null; model = null; Dirty = false;
        }

        internal void ReloadModel()
        {
            model = null; Dirty = true;
            for (int i = 0; i < rendered.Length; i++) { currentStamps[i] = null; rendered[i] = null; }
        }
    }
}
