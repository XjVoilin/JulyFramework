using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Rendering.URP.Editor
{
    internal sealed partial class MatCapWindow
    {
        const string TexturePresetsLabel = "July 预设";
        const string ProjectTexturesLabel = "项目贴图";
        const string LightCardFlashTooltip = "点击让贴图和有 MatCap 模型中的对应反光闪一下";
        const double LightCardFlashDuration = .6;
        const float LightCardHeaderHeight = 24;
        const float MinLightCardSize = .015f;
        const float MaxLightCardSize = 3f;
        static readonly string[] LightShapes = { "方形", "圆形", "柔光" };
        static readonly GUIContent LightCardOptions = new GUIContent("⋮", "光源操作");
        static readonly GUIContent LightCardEnabled = new GUIContent("", "启用光源，控制它在预览和导出中的显示。");
        static readonly GUIContent FillStrength = new GUIContent("补光强度", "抬高球面暗部，越接近球缘影响越小。");
        static readonly GUIContent SoftLightRange = new GUIContent("柔光范围", "从最亮处到亮度降为一半的球面角度（度）。");
        static readonly GUIContent AddLightCardLabel = new GUIContent("添加光源");
        static MatCapLightCard? copiedLightCard;

        MatCapTextureEdit textureEdit;
        readonly List<bool> lightCardFoldouts = new List<bool>();
        readonly AdvancedDropdownState textureDropdownState = new AdvancedDropdownState();
        GUIStyle lightCardOptionsStyle;
        Texture2D lightCardFlash;
        Texture2D lightCardFlashFrame;
        double lightCardFlashStarted;
        bool revealNewCard;
        Vector2 recipeScroll;

        void SetTextureSource(Texture2D image, bool followsMaterial = false, bool newReflection = false)
        {
            if (textureEdit.Texture != image || textureEdit.IsNewReflection != newReflection)
                lightCardFoldouts.Clear();
            textureEdit.SetSource(image, followsMaterial, newReflection);
            ResetRecipeUI();
        }

        void RestoreParameters()
        {
            RecordEdit("还原 MatCap 参数");
            textureEdit.Restore();
            ResetRecipeUI();
        }

        void ResetRecipeUI()
        {
            SyncLightCardFoldouts();
            recipeScroll = Vector2.zero;
            revealNewCard = false;
            InvalidatePreview();
        }

        void SyncLightCardFoldouts()
        {
            int count = textureEdit.LightCards.Count;
            if (lightCardFoldouts.Count > count)
                lightCardFoldouts.RemoveRange(count, lightCardFoldouts.Count - count);
            while (lightCardFoldouts.Count < count)
                lightCardFoldouts.Add(lightCardFoldouts.Count == 0);
        }

        void StartReflectionRecipe()
        {
            RecordEdit("新建反射球");
            SetTextureSource(null, newReflection: true);
        }

        void DrawInspector(float flashWeight)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSectionTitle("MatCap 贴图");
                if (GUILayout.Button("新建反射球", GUILayout.Width(90), GUILayout.Height(22))) StartReflectionRecipe();
            }
            DrawTextureSelection();
            EditorGUILayout.Space(6);
            DrawTexturePreview(flashWeight);
            EditorGUILayout.Space(6);
            DrawRecipe();
            DrawOutputActions();
        }

        void DrawTexturePreview(float flashWeight)
        {
            Texture2D image = textureEdit.Result();
            float size = Mathf.Clamp(position.height * .16f, 82, 112);
            Rect previewArea = GUILayoutUtility.GetRect(1, size + 16, GUILayout.ExpandWidth(true));
            DrawSurface(previewArea, Viewport);
            var sample = new Rect(previewArea.x + 8, previewArea.y + 8, size, size);
            EditorGUI.DrawRect(sample, new Color(.09f, .11f, .13f));
            if (image != null) GUI.DrawTexture(sample, image, ScaleMode.ScaleToFit, false);
            if (flashWeight > 0 && lightCardFlash != null) DrawFlashTexture(sample, lightCardFlash, flashWeight);

            float labelX = sample.xMax + 13;
            bool hasDraft = textureEdit.HasDraft;
            float labelY = previewArea.center.y - (hasDraft ? 27 : 18);
            float labelWidth = previewArea.xMax - labelX - 8;
            string kind = textureEdit.UsesReflectionCards ? "反射球" : "贴图预览";
            string dimensions = image == null ? "暂无贴图" : $"{image.width} × {image.height}";
            GUI.Label(new Rect(labelX, labelY, labelWidth, 19), kind, EditorStyles.whiteBoldLabel);
            GUI.Label(new Rect(labelX, labelY + 19, labelWidth, 17), dimensions, EditorStyles.whiteMiniLabel);
            if (hasDraft)
            {
                var draftMark = new Rect(labelX, labelY + 43, 5, 5);
                EditorGUI.DrawRect(draftMark, new Color32(105, 169, 190, 255));
                GUI.Label(new Rect(labelX + 10, labelY + 37, labelWidth - 10, 17), "待生成", EditorStyles.whiteMiniLabel);
            }
        }

        void DrawRecipe()
        {
            if (!textureEdit.UsesReflectionCards)
            {
                string message = textureEdit.Texture != null ? "这张贴图没有光源配方。可选择预设或新建反射球开始制作。" :
                    textureEdit.FollowsMaterial && HasMaterial ? "此材质没有配置 MatCap。" : "请选择 MatCap 贴图或配方。";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                GUILayout.FlexibleSpace();
                return;
            }

            DrawSectionTitle("反射球光源");
            if (revealNewCard && Event.current.type == EventType.Layout)
            {
                recipeScroll.y = textureEdit.LightCards.Count * 256;
                revealNewCard = false;
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(recipeScroll, GUILayout.ExpandHeight(true)))
            {
                recipeScroll = scroll.scrollPosition;
                float labelWidth = EditorGUIUtility.labelWidth;
                float fieldWidth = EditorGUIUtility.fieldWidth;
                try
                {
                    EditorGUIUtility.labelWidth = 82;
                    EditorGUIUtility.fieldWidth = 48;
                    DrawLightCards();
                }
                finally
                {
                    EditorGUIUtility.labelWidth = labelWidth;
                    EditorGUIUtility.fieldWidth = fieldWidth;
                }
            }

            DrawAddLightCard();
        }

        void DrawOutputActions()
        {
            EditorGUILayout.Space(5);
            DrawRule();
            EditorGUILayout.Space(6);
            using var row = new EditorGUILayout.HorizontalScope();
            using (new EditorGUI.DisabledScope(!textureEdit.HasDraft))
                if (GUILayout.Button("还原参数", GUILayout.Width(90), GUILayout.Height(25))) RestoreParameters();
            bool canGenerate = textureEdit.CanGenerate;
            using var disabled = new EditorGUI.DisabledScope(!canGenerate);
            Color background = GUI.backgroundColor;
            if (canGenerate) GUI.backgroundColor = new Color(.61f, .86f, .95f);
            try
            {
                if (GUILayout.Button("生成 MatCap", GUILayout.Height(25))) SaveCandidate();
            }
            finally { GUI.backgroundColor = background; }
        }

        void DrawLightCards()
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                float fill = EditorGUILayout.Slider(FillStrength, textureEdit.fill, 0f, 1f);
                if (check.changed)
                {
                    RecordEdit("调整 MatCap 补光", continuous: true);
                    textureEdit.fill = fill;
                    InvalidateDraft();
                }
            }
            if (textureEdit.LightCards.Count == 0)
                EditorGUILayout.HelpBox("添加光板或柔光开始布光。", MessageType.None);
            for (int i = 0; i < textureEdit.LightCards.Count; i++) DrawLightCard(i);
        }

        void DrawAddLightCard()
        {
            EditorGUILayout.Space(4);
            var style = GUI.skin.button;
            Rect addRect = GUILayoutUtility.GetRect(AddLightCardLabel, style, GUILayout.ExpandWidth(true), GUILayout.Height(23));
            if (GUI.Button(addRect, AddLightCardLabel, style)) AddLightCardMenu().DropDown(addRect);
        }

        void AddLightCard(MatCapLightCard card)
        {
            RecordEdit("添加 MatCap 光源");
            textureEdit.LightCards.Add(card);
            lightCardFoldouts.Add(true);
            revealNewCard = true;
            InvalidateDraft();
            if (!card.hidden) FlashLightCard(textureEdit.LightCards.Count - 1);
            Repaint();
        }

        void DrawLightCard(int index)
        {
            var card = textureEdit.LightCards[index];
            DrawLightCardHeader(index, ref card);
            bool visibilityChanged = card.hidden != textureEdit.LightCards[index].hidden;
            bool parametersChanged = false;
            if (lightCardFoldouts[index])
            {
                using var parameters = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
                using var check = new EditorGUI.ChangeCheckScope();
                DrawLightCardParameters(ref card);
                if (check.changed)
                {
                    card.position = Vector2.ClampMagnitude(card.position, MatCapLightCard.CaptureRadius);
                    parametersChanged = true;
                }
            }
            if (!visibilityChanged && !parametersChanged) return;
            RecordEdit(visibilityChanged ? "切换 MatCap 光源显隐" : "调整 MatCap 光源", continuous: parametersChanged);
            textureEdit.LightCards[index] = card;
            InvalidateDraft();
        }

        void DrawLightCardHeader(int index, ref MatCapLightCard card)
        {
            Rect header = EditorGUILayout.GetControlRect(false, LightCardHeaderHeight);
            EditorGUI.DrawRect(header, lightCardFoldouts[index] ? SelectedSurface : HeaderSurface);
            var foldoutRect = new Rect(header.x + 4, header.y + 3, 15, header.height - 6);
            var enabledRect = new Rect(foldoutRect.xMax + 2, foldoutRect.y, 16, foldoutRect.height);
            var menuRect = new Rect(header.xMax - 25, header.y, 23, header.height);
            var swatch = new Rect(menuRect.x - 18, header.center.y - 5, 10, 10);
            var shape = new Rect(swatch.x - 34, foldoutRect.y, 32, foldoutRect.height);
            lightCardFoldouts[index] = EditorGUI.Foldout(foldoutRect, lightCardFoldouts[index], GUIContent.none);
            card.hidden = !GUI.Toggle(enabledRect, !card.hidden, LightCardEnabled, EditorStyles.toggle);

            using (new EditorGUI.DisabledScope(card.hidden))
            {
                string name = card.shape == MatCapLightShape.Soft ? "柔光" : "光板";
                var title = new GUIContent($"{name} {index + 1}", LightCardFlashTooltip);
                float width = Mathf.Min(EditorStyles.boldLabel.CalcSize(title).x, shape.x - enabledRect.xMax - 3);
                var titleRect = new Rect(enabledRect.xMax + 3, foldoutRect.y, width, foldoutRect.height);
                if (GUI.Button(titleRect, title, EditorStyles.boldLabel)) FlashLightCard(index);
                if (!card.hidden) EditorGUIUtility.AddCursorRect(titleRect, MouseCursor.Link);
                GUI.Label(shape, LightShapes[(int)card.shape], EditorStyles.miniLabel);
            }
            DrawSurface(swatch, card.hidden ? Color.Lerp(card.color, HeaderSurface, .6f) : card.color);

            lightCardOptionsStyle ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            if (GUI.Button(menuRect, LightCardOptions, lightCardOptionsStyle)) LightCardMenu(index).DropDown(menuRect);
        }

        static void DrawLightCardParameters(ref MatCapLightCard card)
        {
            card.shape = (MatCapLightShape)EditorGUILayout.Popup("类型", (int)card.shape, LightShapes);
            const float radius = MatCapLightCard.CaptureRadius;
            card.position.x = EditorGUILayout.Slider("水平位置", card.position.x, -radius, radius);
            card.position.y = EditorGUILayout.Slider("垂直位置", card.position.y, -radius, radius);
            if (card.shape == MatCapLightShape.Soft)
                card.softRange = EditorGUILayout.Slider(SoftLightRange, card.SoftRange, MatCapLightCard.MinSoftRange, MatCapLightCard.MaxSoftRange);
            else
            {
                card.size.x = DrawLightCardSize("宽度", card.size.x);
                card.size.y = DrawLightCardSize("高度", card.size.y);
                card.angle = EditorGUILayout.Slider("旋转", card.angle, -180, 180);
                card.softness = EditorGUILayout.Slider("边缘柔化", card.softness, 0f, 1f);
            }
            card.intensity = EditorGUILayout.Slider("亮度", card.intensity, 0, 2);
            card.color = EditorGUILayout.ColorField(new GUIContent("颜色"), card.color, true, false, false);
        }

        static float DrawLightCardSize(string label, float size)
        {
            Rect row = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(label));
            var field = new Rect(row.xMax - EditorGUIUtility.fieldWidth, row.y, EditorGUIUtility.fieldWidth, row.height);
            var slider = new Rect(row.x, row.y, field.x - row.x - 5, row.height);
            // Square the normalized travel so narrow highlight strips get more drag resolution.
            float position = Mathf.Sqrt(Mathf.InverseLerp(MinLightCardSize, MaxLightCardSize, size));
            float next = GUI.HorizontalSlider(slider, position, 0f, 1f);
            if (next != position) size = Mathf.Lerp(MinLightCardSize, MaxLightCardSize, next * next);

            float entered = EditorGUI.DelayedFloatField(field, size);
            return entered != size && !float.IsNaN(entered) ? Mathf.Clamp(entered, MinLightCardSize, MaxLightCardSize) : size;
        }

        GenericMenu AddLightCardMenu()
        {
            var square = MatCapLightCard.New;
            var round = square;
            round.shape = MatCapLightShape.Ellipse;
            var soft = square;
            soft.shape = MatCapLightShape.Soft;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("方形光板"), false, () => AddLightCard(square));
            menu.AddItem(new GUIContent("圆形光板"), false, () => AddLightCard(round));
            menu.AddItem(new GUIContent("球面柔光"), false, () => AddLightCard(soft));
            if (copiedLightCard is MatCapLightCard copied)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("粘贴为新光源"), false, () => AddLightCard(copied));
            }
            return menu;
        }

        GenericMenu LightCardMenu(int index)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("移除"), false, () =>
            {
                RecordEdit("移除 MatCap 光源");
                textureEdit.LightCards.RemoveAt(index);
                lightCardFoldouts.RemoveAt(index);
                InvalidateDraft();
                Repaint();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("复制"), false, () => copiedLightCard = textureEdit.LightCards[index]);
            var paste = new GUIContent("粘贴");
            var pasteNew = new GUIContent("粘贴为新光源");
            if (copiedLightCard is MatCapLightCard copied)
            {
                menu.AddItem(paste, false, () =>
                {
                    if (copied.Equals(textureEdit.LightCards[index])) return;
                    RecordEdit("粘贴 MatCap 光源");
                    textureEdit.LightCards[index] = copied;
                    InvalidateDraft();
                    Repaint();
                });
                menu.AddItem(pasteNew, false, () => AddLightCard(copied));
            }
            else
            {
                menu.AddDisabledItem(paste);
                menu.AddDisabledItem(pasteNew);
            }
            return menu;
        }

        void DrawTextureSelection()
        {
            string label = "选择 MatCap";
            if (textureEdit.Texture != null)
            {
                string category = textureEdit.IsBuiltInTexture ? TexturePresetsLabel : ProjectTexturesLabel;
                label = $"{category} · {textureEdit.Texture.name}";
            }
            else if (textureEdit.IsNewReflection) label = "新建反射球 · 配方";
            else if (textureEdit.FollowsMaterial && HasMaterial) label = "MatCap 未配置";
            Rect rect = EditorGUILayout.GetControlRect(false);
            if (!EditorGUI.DropdownButton(rect, new GUIContent(label, label), FocusType.Keyboard, EditorStyles.popup)) return;
            new MatCapTextureDropdown(textureDropdownState, BuildTextureChoices(), ChooseTexture).Show(rect);
        }

        AdvancedDropdownItem BuildTextureChoices()
        {
            var root = new AdvancedDropdownItem("选择 MatCap");
            var builtIn = new AdvancedDropdownItem(TexturePresetsLabel);
            var project = new AdvancedDropdownItem(ProjectTexturesLabel);
            root.AddChild(builtIn);
            root.AddChild(project);
            var assignedImage = GetMaterialMatCap(material);
            var packageTextures = new HashSet<Texture2D>();
            var defaultTexture = MatCapTextureAssets.DefaultTexture;
            if (defaultTexture != null)
            {
                packageTextures.Add(defaultTexture);
                AddChoice(builtIn, $"默认 · {defaultTexture.name}", defaultTexture);
            }
            foreach (var preset in MatCapPresetCatalog.TexturePresets)
            {
                var image = preset.Texture;
                if (image == null) continue;
                packageTextures.Add(image);
                if (preset.RecipeFile != null) AddChoice(builtIn, image.name, image);
            }

            var projectTextures = MatCapTextureAssets.FindProjectTextures().Concat(MatCapRecipe.ProjectTextures())
                .Append(assignedImage)
                .Append(textureEdit.Texture)
                .Where(image => image != null && !packageTextures.Contains(image)).Distinct()
                .OrderBy(image => image.name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var image in projectTextures)
                AddChoice(project, image.name, image);
            if (projectTextures.Length == 0) project.AddChild(new AdvancedDropdownItem("暂无项目贴图") { enabled = false });
            return root;

            void AddChoice(AdvancedDropdownItem group, string label, Texture2D image) =>
                group.AddChild(new MatCapTextureDropdown.Item(label, image, image == assignedImage, image == textureEdit.Texture));
        }

        void ChooseTexture(Texture2D image, bool assigned)
        {
            if (image == textureEdit.Texture && (!assigned || textureEdit.FollowsMaterial)) return;
            RecordEdit("选择 MatCap 贴图");
            if (assigned)
            {
                ResetMaterialParameters();
                SelectMaterialMatCap();
            }
            else SelectTexture(image);
            Repaint();
        }

        void FlashLightCard(int index)
        {
            ClearLightCardFlash();
            lightCardFlash = textureEdit.RenderWithoutLight(index);
            lightCardFlashStarted = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void ClearLightCardFlash()
        {
            Object.DestroyImmediate(lightCardFlash);
            lightCardFlash = null;
            ClearLightCardFlashFrame();
        }

        void ClearLightCardFlashFrame()
        {
            Object.DestroyImmediate(lightCardFlashFrame);
            lightCardFlashFrame = null;
        }

        float LightCardFlashWeight()
        {
            if (lightCardFlash == null) return 0;
            double elapsed = EditorApplication.timeSinceStartup - lightCardFlashStarted;
            if (elapsed >= LightCardFlashDuration)
            {
                ClearLightCardFlash();
                return 0;
            }
            const double hold = .1;
            return .82f * (1 - Mathf.SmoothStep(0, 1, (float)((elapsed - hold) / (LightCardFlashDuration - hold))));
        }

        static void DrawFlashTexture(Rect area, Texture2D image, float weight)
        {
            var color = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * weight);
            GUI.DrawTexture(area, image, ScaleMode.ScaleToFit, true);
            GUI.color = color;
        }

        void SaveCandidate()
        {
            string name = textureEdit.Texture != null ? textureEdit.Texture.name + "_01" : "Matcap_ReflectionSphere";
            if (!name.StartsWith(MatCapTextureAssets.TexturePrefix, StringComparison.Ordinal))
                name = MatCapTextureAssets.TexturePrefix + name;
            string path = EditorUtility.SaveFilePanelInProject("生成 MatCap", name, "png", "保存 PNG 和对应的生成配方");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var saved = textureEdit.Save(path);
                RecordEdit("切换至生成的 MatCap");
                SelectTexture(saved);
                Report($"已生成 {saved.name} 和 JSON 配方。", MessageType.Info);
            }
            catch (InvalidOperationException e) { Report(e.Message, MessageType.Warning); }
            catch (IOException e) { Report(e.Message, MessageType.Error); }
            catch (UnityException e) { Report(e.Message, MessageType.Error); }
        }
    }
}
