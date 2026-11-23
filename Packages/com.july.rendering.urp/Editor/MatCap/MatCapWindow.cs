using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    internal sealed partial class MatCapWindow : EditorWindow
    {
        internal const string MenuPath = "JulyGF/渲染/MatCap 制作与预览";
        const string LastModelPath = "Library/July/MatCapAuthoring/last-model.txt";
        const float PanelGap = 10;
        const float ToolbarHeight = 42;
        const float FrameHeaderHeight = 24;
        static readonly Vector2 MinimumSize = new Vector2(780, 520);
        static readonly GUIContent MaterialPreviewEnabled = new GUIContent("", "参与右侧 MatCap 预览。取消勾选时仍显示该材质。");
        static readonly GUIContent UseSelectedModelLabel = new GUIContent("使用选中资源", "在 Project 中选中 Prefab 或 FBX。");

        GameObject model;
        Material material;
        Vector2 rotation = new Vector2(-20, -20);

        readonly Dictionary<Material, MatCapTextureEdit> materialEdits = new Dictionary<Material, MatCapTextureEdit>();
        MatCapPreview preview;
        GUIStyle sectionTitleStyle;
        GUIStyle materialRowStyle;
        int editUndoControl;
        int editUndoGroup;
        bool setupDirty = true, frameDirty = true;
        Vector2 materialScroll;
        Vector2Int frameSize;
        string notice;
        MessageType noticeType;

        [MenuItem(MenuPath, false, 1000)]
        internal static void Open()
        {
            var window = GetWindow<MatCapWindow>("MatCap 制作与预览");
            window.minSize = MinimumSize;
            window.Show();
        }

        MatCapSettings Settings => MatCapSettings.Instance;
        bool HasMaterial => MatCapMaterial.IsCompatible(material);

        static bool UsesOriginalMaterial(Material target, MatCapTextureEdit edit) =>
            edit.FollowsMaterial && !edit.UsesReflectionCards && edit.Texture == GetMaterialMatCap(target) &&
            Mathf.Approximately(edit.strength, target.GetFloat(MatCapMaterial.Strength)) && Mathf.Approximately(edit.darkRetention, MatCapMaterial.ReadDarkRetention(target));

        void OnEnable()
        {
            minSize = MinimumSize;
            var lastModel = LoadLastModel();
            if (lastModel != null)
            {
                SelectModel(lastModel, MatCapPresetCatalog.ModelPresetFor(lastModel)?.Material);
            }
            else
            {
                var initial = MatCapPresetCatalog.BuiltIn.Select(preset => (preset, model: preset.Model))
                    .FirstOrDefault(entry => entry.model != null);
                SelectModel(initial.model, initial.preset?.Material);
            }

            if (material != null) SelectMaterialMatCap();
            else SelectTexture(MatCapTextureAssets.DefaultTexture);
            Undo.undoRedoPerformed += RefreshAfterUndo;
        }

        static GameObject LoadLastModel()
        {
            if (!File.Exists(LastModelPath)) return null;
            var savedModel = MatCapPresetCatalog.LoadAsset<GameObject>(File.ReadAllText(LastModelPath).Trim());
            return IsModelAsset(savedModel) ? savedModel : null;
        }

        void RememberModel()
        {
            string guid = model == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(model));
            Directory.CreateDirectory(Path.GetDirectoryName(LastModelPath));
            File.WriteAllText(LastModelPath, guid);
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshAfterUndo;
            ClearLightCardFlash();
            preview?.Dispose();
            preview = null;
            textureEdit?.Dispose();
            textureEdit = null;
            DisposeInactiveEdits();
        }

        void OnProjectChange()
        {
            RefreshPreviewMaterials();
            if (!textureEdit.IsNewReflection && !textureEdit.HasDraft) RefreshTexture();
            Repaint();
        }

        void RefreshPreviewMaterials()
        {
            preview?.InvalidateMutedMaterials();
            foreach (var edit in materialEdits)
            {
                if (MatCapMaterial.IsCompatible(edit.Key))
                    UpdateRetainedMaterial(edit.Key, edit.Value);
                else
                    preview.ReleaseMaterial(edit.Key);
            }
            setupDirty = frameDirty = true;
        }

        void RecordEdit(string action, bool continuous = false)
        {
            int control = continuous ? GUIUtility.hotControl : 0;
            if (control != 0 && control == editUndoControl && editUndoGroup == Undo.GetCurrentGroup()) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RegisterCompleteObjectUndo(textureEdit, action);
            editUndoControl = control;
            editUndoGroup = Undo.GetCurrentGroup();
        }

        void RefreshAfterUndo()
        {
            editUndoControl = 0;
            textureEdit.ReloadBaseline();
            foreach (var edit in materialEdits.Values) edit.ReloadBaseline();
            RefreshPreviewMaterials();
            SyncLightCardFoldouts();
            InvalidatePreview();
            Repaint();
        }

        void OnSelectionChange() => Repaint();

        void RefreshTexture()
        {
            if (textureEdit.FollowsMaterial) SelectMaterialMatCap();
            else SelectTexture(textureEdit.Texture != null ? textureEdit.Texture : MatCapTextureAssets.DefaultTexture);
        }

        void SelectTexture(Texture2D image) => SetTextureSource(image);

        void SelectPreset(MatCapModelPreset preset, GameObject selection)
        {
            SelectModel(selection, preset.Material);
            SelectMaterialMatCap();
            RememberModel();
        }

        void SelectCustomModel(GameObject selection)
        {
            if (selection != null && !IsModelAsset(selection))
            {
                Report("请选择 Project 中的 Prefab 或 FBX。", MessageType.Info);
                return;
            }
            SelectModel(selection);
            FollowMaterialMatCap();
            RememberModel();
        }

        static bool IsModelAsset(GameObject selection)
        {
            if (selection == null || !AssetDatabase.IsMainAsset(selection)) return false;
            string extension = Path.GetExtension(AssetDatabase.GetAssetPath(selection));
            return extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        static Texture2D GetMaterialMatCap(Material target) =>
            MatCapMaterial.IsCompatible(target) ? target.GetTexture(MatCapMaterial.Texture) as Texture2D : null;

        void SelectMaterialMatCap() => SetTextureSource(GetMaterialMatCap(material), followsMaterial: true);

        void FollowMaterialMatCap()
        {
            if (!textureEdit.FollowsMaterial) return;
            if (textureEdit.HasDraft) textureEdit.FollowsMaterial = false;
            else SelectMaterialMatCap();
        }

        void SelectModel(GameObject selection, Material preferred = null)
        {
            if (textureEdit == null) textureEdit = MatCapTextureEdit.Create();
            else Undo.ClearUndo(textureEdit);
            editUndoControl = 0;
            preview?.Dispose();
            DisposeInactiveEdits();
            model = selection;
            preview = model != null ? new MatCapPreview(model) : null;
            var target = preview != null && preview.Materials.Contains(preferred) && MatCapMaterial.IsCompatible(preferred)
                ? preferred : preview?.Materials.Where(MatCapMaterial.IsCompatible)
                    .OrderByDescending(item => GetMaterialMatCap(item) != null).FirstOrDefault();
            material = target;
            ResetMaterialParameters();
            InvalidatePreview();
            if (preview != null) LoadInactiveMaterialRecipes();
            materialScroll = Vector2.zero;
        }

        void LoadInactiveMaterialRecipes()
        {
            foreach (var item in preview.Materials)
            {
                if (item == material) continue;
                var image = GetMaterialMatCap(item);
                if (image == null) continue;
                var edit = MatCapTextureEdit.Create();
                edit.SetSource(image, followsMaterial: true);
                if (!edit.UsesReflectionCards)
                {
                    edit.Dispose();
                    continue;
                }
                edit.strength = item.GetFloat(MatCapMaterial.Strength);
                edit.darkRetention = MatCapMaterial.ReadDarkRetention(item);
                RetainMaterialEdit(item, edit);
            }
        }

        void RetainMaterialEdit(Material target, MatCapTextureEdit edit)
        {
            materialEdits.Add(target, edit);
            UpdateRetainedMaterial(target, edit);
        }

        void UpdateRetainedMaterial(Material target, MatCapTextureEdit edit)
        {
            if (UsesOriginalMaterial(target, edit)) preview.ReleaseMaterial(target);
            else preview.RetainMaterial(target, edit.Result(), edit.strength, edit.darkRetention);
        }

        void SelectMaterial(Material target)
        {
            if (target == material) return;
            if (HasMaterial) RetainMaterialEdit(material, textureEdit);
            else textureEdit.Dispose();
            material = target;
            editUndoControl = 0;
            lightCardFoldouts.Clear();
            if (!materialEdits.Remove(target, out textureEdit))
            {
                textureEdit = MatCapTextureEdit.Create();
                ResetMaterialParameters();
                SelectMaterialMatCap();
                return;
            }

            preview.ReleaseMaterial(target);
            ResetRecipeUI();
        }

        void DisposeInactiveEdits()
        {
            foreach (var edit in materialEdits.Values) edit.Dispose();
            materialEdits.Clear();
        }

        void ResetMaterialParameters()
        {
            textureEdit.strength = HasMaterial ? material.GetFloat(MatCapMaterial.Strength) : 1;
            textureEdit.darkRetention = HasMaterial ? MatCapMaterial.ReadDarkRetention(material) : 0;
        }

        void InvalidateDraft()
        {
            textureEdit.Invalidate();
            InvalidatePreview();
        }

        void InvalidatePreview()
        {
            ClearLightCardFlash();
            setupDirty = frameDirty = true;
        }

        void OnGUI()
        {
            if (Event.current.rawType == EventType.MouseDown) editUndoControl = 0;
            bool repainting = Event.current.type == EventType.Repaint;
            float flashWeight = repainting ? LightCardFlashWeight() : 0;
            var window = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(window, Workspace);
            var content = Inset(window, PanelGap, PanelGap);
            var toolbar = new Rect(content.x, content.y, content.width, ToolbarHeight);
            DrawToolbar(toolbar);

            float bottom = content.yMax;
            if (!string.IsNullOrEmpty(notice))
            {
                float height = Mathf.Max(24, EditorStyles.helpBox.CalcHeight(new GUIContent(notice), content.width - 28));
                EditorGUI.HelpBox(new Rect(content.x, bottom - height, content.width, height), notice, noticeType);
                bottom -= height + PanelGap;
            }

            float top = toolbar.yMax + PanelGap;
            float inspectorWidth = Mathf.Clamp(content.width * .39f, 292, 430);
            var modelPanel = Rect.MinMaxRect(content.x, top, content.xMax - inspectorWidth - PanelGap, bottom);
            var inspector = Rect.MinMaxRect(modelPanel.xMax + PanelGap, top, content.xMax, bottom);
            DrawSurface(modelPanel, Surface);
            DrawSurface(inspector, Surface);
            using (new GUILayout.AreaScope(Inset(modelPanel, PanelGap, PanelGap))) DrawModel(flashWeight);
            using (new GUILayout.AreaScope(Inset(inspector, PanelGap, PanelGap))) DrawInspector(flashWeight);
            if (repainting && lightCardFlash != null) Repaint();
        }

        static Color Workspace => Theme(new Color32(36, 40, 45, 255), new Color32(207, 213, 220, 255));
        static Color Surface => Theme(new Color32(46, 52, 59, 255), new Color32(236, 239, 243, 255));
        static Color HeaderSurface => Theme(new Color32(54, 61, 69, 255), new Color32(221, 226, 232, 255));
        static Color Rule => Theme(new Color32(72, 80, 89, 255), new Color32(181, 190, 200, 255));
        static Color Accent => Theme(new Color32(105, 169, 190, 255), new Color32(51, 116, 140, 255));
        static Color SelectedSurface => Theme(new Color32(53, 78, 92, 255), new Color32(196, 220, 230, 255));
        static Color Viewport => new Color(.17f, .19f, .22f);

        static Color Theme(Color32 dark, Color32 light) => EditorGUIUtility.isProSkin ? dark : light;

        static Rect Inset(Rect area, float horizontal, float vertical) =>
            Rect.MinMaxRect(area.xMin + horizontal, area.yMin + vertical, area.xMax - horizontal, area.yMax - vertical);

        static void DrawSurface(Rect area, Color fill)
        {
            EditorGUI.DrawRect(area, Rule);
            EditorGUI.DrawRect(Inset(area, 1, 1), fill);
        }

        static void DrawRule()
        {
            Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, Rule);
        }

        void DrawSectionTitle(string title)
        {
            sectionTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            GUILayout.Label(title, sectionTitleStyle, GUILayout.Height(22), GUILayout.ExpandWidth(true));
        }

        void DrawToolbar(Rect area)
        {
            DrawSurface(area, HeaderSurface);
            using var toolbar = new GUILayout.AreaScope(Inset(area, 11, 11));
            using var row = new EditorGUILayout.HorizontalScope();
            GUILayout.Label("模型", EditorStyles.boldLabel, GUILayout.Width(32));
            var selected = (GameObject)EditorGUILayout.ObjectField(model, typeof(GameObject), false, GUILayout.ExpandWidth(true));
            if (selected != model) SelectCustomModel(selected);
            var selectedAsset = Selection.activeObject as GameObject;
            using (new EditorGUI.DisabledScope(!IsModelAsset(selectedAsset)))
                if (GUILayout.Button(UseSelectedModelLabel, GUILayout.Width(92))) SelectCustomModel(selectedAsset);
            GUILayout.Space(6);
            DrawPresetSelection();
            GUILayout.Space(8);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1, 18, GUILayout.Width(1)), Rule);
            GUILayout.Space(5);
            string light = string.IsNullOrEmpty(Settings.light.nodeName) ? "尚未同步光照" : Settings.light.nodeName;
            GUILayout.Label(new GUIContent(light, light), EditorStyles.miniLabel, GUILayout.Width(111));
            if (GUILayout.Button("同步", GUILayout.Width(48))) SyncLighting();
        }

        void DrawPresetSelection()
        {
            GUILayout.Label("July 预设", GUILayout.Width(54));
            var presets = MatCapPresetCatalog.BuiltIn.Select(preset => (preset, model: preset.Model))
                .Where(entry => entry.model != null)
                .Select(entry => (entry.preset, entry.model, label: string.IsNullOrEmpty(entry.preset.label) ? entry.model.name : entry.preset.label))
                .ToArray();
            string label = presets.FirstOrDefault(entry => entry.model == model).label ?? "选择预设";
            var content = new GUIContent(label, label);
            Rect rect = GUILayoutUtility.GetRect(content, EditorStyles.popup, GUILayout.Width(166));
            using (new EditorGUI.DisabledScope(presets.Length == 0))
            {
                if (!EditorGUI.DropdownButton(rect, content, FocusType.Keyboard, EditorStyles.popup)) return;
            }

            var menu = new GenericMenu();
            foreach (var entry in presets)
            {
                menu.AddItem(new GUIContent(entry.label), entry.model == model, () =>
                {
                    SelectPreset(entry.preset, entry.model);
                    Repaint();
                });
            }
            menu.DropDown(rect);
        }

        void SyncLighting()
        {
            var light = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Light>() : null;
            try
            {
                Settings.Sync(light);
                frameDirty = true;
                Report("光照参数已同步到 July/MatCapAuthoring/settings.json。", MessageType.Info);
            }
            catch (InvalidOperationException e) { Report(e.Message, MessageType.Warning); }
            catch (IOException e) { Report(e.Message, MessageType.Error); }
        }

        void DrawModel(float flashWeight)
        {
            DrawSectionTitle("模型预览");
            Rect rect = GUILayoutUtility.GetRect(150, 120, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            const int gutter = 6;
            float width = (rect.width - gutter) * .5f;
            var left = new Rect(rect.x, rect.y, width, rect.height);
            var right = new Rect(left.xMax + gutter, rect.y, width, rect.height);
            if (preview != null && preview.HasGeometry)
            {
                DrawRotation(rect);
                EditorGUIUtility.AddCursorRect(FrameImageArea(left), MouseCursor.Orbit);
                EditorGUIUtility.AddCursorRect(FrameImageArea(right), MouseCursor.Orbit);
                if (Event.current.type == EventType.Repaint)
                {
                    Rect viewport = FrameImageArea(left);
                    RenderFrames(viewport.width, viewport.height, flashWeight);
                }
            }
            DrawFrame(left, "无 MatCap", preview?.Without);
            DrawFrame(right, "有 MatCap", preview?.With, true, lightCardFlashFrame, flashWeight);
            if (preview == null) EditorGUILayout.HelpBox("选择 Prefab、FBX 或 July 预设。", MessageType.None);
            else if (!preview.HasGeometry) EditorGUILayout.HelpBox("所选对象没有可预览的网格。", MessageType.Info);
            DrawMaterials();
        }

        void DrawRotation(Rect rect)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = id;
                evt.Use();
            }
            if (GUIUtility.hotControl != id) return;
            if (evt.type == EventType.MouseDrag)
            {
                rotation.x = Mathf.Clamp(rotation.x + evt.delta.y * .5f, -89, 89);
                rotation.y -= evt.delta.x * .5f;
                frameDirty = true;
                Repaint();
                evt.Use();
            }
            if (evt.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        void RenderFrames(float width, float height, float flashWeight)
        {
            var size = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(width), 16, 720), Mathf.Clamp(Mathf.RoundToInt(height), 16, 720));
            if (setupDirty)
            {
                preview.Setup(material, textureEdit.Result(), textureEdit.strength, textureEdit.darkRetention,
                    HasMaterial && UsesOriginalMaterial(material, textureEdit));
                setupDirty = false;
            }
            if (frameDirty || frameSize != size)
            {
                preview.Render(size.x, size.y, rotation, Settings.light);
                frameDirty = false;
                frameSize = size;
                ClearLightCardFlashFrame();
            }
            if (flashWeight > 0 && HasMaterial && preview.IsMatCapEnabled(material) && lightCardFlashFrame == null)
                lightCardFlashFrame = preview.RenderLightCardFlash(size.x, size.y, lightCardFlash);
        }

        static Rect FrameImageArea(Rect frame) => Rect.MinMaxRect(frame.x + 1, frame.y + FrameHeaderHeight, frame.xMax - 1, frame.yMax - 1);

        static void DrawFrame(Rect rect, string label, Texture2D image, bool withMatCap = false, Texture2D flash = null, float flashWeight = 0)
        {
            DrawSurface(rect, Viewport);
            var header = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, FrameHeaderHeight - 1);
            EditorGUI.DrawRect(header, withMatCap ? SelectedSurface : HeaderSurface);
            if (withMatCap) EditorGUI.DrawRect(new Rect(header.x, header.y, 2, header.height), Accent);
            var caption = new GUIContent(label, withMatCap ? "合成已勾选材质的 MatCap 效果。" : "关闭整个模型所有材质的 MatCap。");
            GUI.Label(new Rect(header.x + 8, header.y + 2, header.width - 16, 19), caption, EditorStyles.miniBoldLabel);
            Rect viewport = FrameImageArea(rect);
            if (image != null) GUI.DrawTexture(viewport, image, ScaleMode.ScaleToFit);
            if (flashWeight > 0 && flash != null) DrawFlashTexture(viewport, flash, flashWeight);
        }

        void DrawMaterials()
        {
            if (preview == null || preview.Materials.Count == 0) return;
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSectionTitle("材质");
                GUILayout.Label("参与预览", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(58), GUILayout.Height(22));
            }
            const float rowHeight = 23;
            materialRowStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(9, 7, 0, 0)
            };
            using (var list = new EditorGUILayout.ScrollViewScope(materialScroll,
                       GUILayout.Height(Mathf.Clamp(preview.Materials.Count * rowHeight + 3, 26, 76))))
            {
                materialScroll = list.scrollPosition;
                foreach (var item in preview.Materials)
                    DrawMaterialRow(item, GUILayoutUtility.GetRect(1, rowHeight, GUILayout.ExpandWidth(true)));
            }
            EditorGUILayout.Space(4);
            if (HasMaterial)
            {
                var range = MatCapMaterial.StrengthRange(material);
                using var check = new EditorGUI.ChangeCheckScope();
                float strength = EditorGUILayout.Slider("MatCap 强度", textureEdit.strength, range.x, range.y);
                if (check.changed)
                {
                    RecordEdit("调整 MatCap 强度", continuous: true);
                    textureEdit.strength = strength;
                    setupDirty = frameDirty = true;
                }
            }
            DrawDarkRetention();
            EditorGUILayout.Space(3);
            DrawRule();
            EditorGUILayout.Space(6);
            string applyPath = EditableMaterialPath();
            if (applyPath != null)
                EditorGUILayout.LabelField(new GUIContent($"应用目标 · {applyPath}", $"共享材质：{applyPath}\n使用此材质的对象会一并更新。"), EditorStyles.miniLabel);
            else if (HasMaterial)
                EditorGUILayout.LabelField("应用目标需为项目中可编辑的 .mat 资产。", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(textureEdit.HasDraft || textureEdit.Texture == null || applyPath == null))
                if (GUILayout.Button("应用到材质", GUILayout.Height(25))) ApplyMaterial();
        }

        void DrawMaterialRow(Material item, Rect row)
        {
            string error = MatCapMaterial.GetError(item);
            bool compatible = error == null;
            bool current = item == material;
            EditorGUI.DrawRect(new Rect(row.x, row.y, row.width, row.height - 2), current ? SelectedSurface : HeaderSurface);
            if (current) EditorGUI.DrawRect(new Rect(row.x, row.y, 2, row.height - 2), Accent);

            using var disabled = new EditorGUI.DisabledScope(!compatible);
            string caption = compatible ? item.name : $"{item.name}  ·  {error}";
            var nameRect = new Rect(row.x, row.y, row.width - 58, row.height);
            if (GUI.Toggle(nameRect, current, new GUIContent(caption, caption), materialRowStyle) && !current) SelectMaterial(item);

            bool enabled = compatible && preview.IsMatCapEnabled(item);
            var checkbox = new Rect(row.xMax - 37, row.y + 3, 17, 17);
            bool show = GUI.Toggle(checkbox, enabled, MaterialPreviewEnabled, EditorStyles.toggle);
            if (show != enabled) SetMaterialPreviewEnabled(item, show);
        }

        void SetMaterialPreviewEnabled(Material item, bool enabled)
        {
            preview.SetMatCapEnabled(item, enabled);
            setupDirty = frameDirty = true;
            Repaint();
        }

        void DrawDarkRetention()
        {
            float? reference = MatCapPresetCatalog.DarkRetentionFor(material);
            bool hasRetention = HasMaterial && MatCapMaterial.HasDarkRetention(material);
            if (!hasRetention && !reference.HasValue) return;
            using var row = new EditorGUILayout.HorizontalScope();
            if (hasRetention)
            {
                using var check = new EditorGUI.ChangeCheckScope();
                float percent = EditorGUILayout.Slider("暗面反光保留率 (%)", textureEdit.darkRetention * 100, 0, 100);
                if (check.changed)
                {
                    RecordEdit("调整暗面反光保留率", continuous: true);
                    textureEdit.darkRetention = percent / 100;
                    setupDirty = frameDirty = true;
                }
            }
            if (reference.HasValue)
            {
                if (!hasRetention) GUILayout.FlexibleSpace();
                GUILayout.Label($"预设参考 {reference.Value * 100:0.#}%", EditorStyles.miniLabel, GUILayout.Width(86));
            }
        }

        string EditableMaterialPath()
        {
            if (!HasMaterial || !AssetDatabase.IsMainAsset(material)) return null;
            string path = AssetDatabase.GetAssetPath(material);
            return path.StartsWith("Assets/", StringComparison.Ordinal) &&
                   Path.GetExtension(path).Equals(".mat", StringComparison.OrdinalIgnoreCase) &&
                   AssetDatabase.IsOpenForEdit(material) ? path : null;
        }

        void ApplyMaterial()
        {
            var target = material;
            Undo.RecordObject(target, "应用 MatCap");
            MatCapMaterial.Apply(target, textureEdit.Texture, textureEdit.strength, textureEdit.darkRetention);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            Report($"已应用到材质 {target.name}。", MessageType.Info);
        }

        void Report(string message, MessageType type)
        {
            notice = message;
            noticeType = type;
            Repaint();
        }
    }
}
