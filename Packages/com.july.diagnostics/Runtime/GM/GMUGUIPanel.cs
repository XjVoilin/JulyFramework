#if JULYGF_DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using July.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace July.Diagnostics
{
    [RequireComponent(typeof(CanvasRenderer))]
    sealed class NonDrawingGraphic : Graphic
    {
        public override void SetMaterialDirty() { }
        public override void SetVerticesDirty() { }
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }

    public sealed class GMUGUIPanel : MonoBehaviour
    {
        #region Theme

        static readonly Color s_bg     = new Color32(18, 18, 24, 255);
        static readonly Color s_header = new Color32(24, 24, 32, 255);
        static readonly Color s_card   = new Color32(30, 32, 40, 255);
        static readonly Color s_input  = new Color32(40, 42, 54, 255);
        static readonly Color s_tabN   = new Color32(38, 40, 50, 255);
        static readonly Color s_tabA   = new Color32(56, 130, 246, 255);
        static readonly Color s_sep    = new Color32(48, 50, 64, 255);
        static readonly Color s_run    = new Color32(46, 160, 67, 255);
        static readonly Color s_close  = new Color32(180, 50, 50, 255);
        static readonly Color s_text   = new Color32(220, 222, 230, 255);
        static readonly Color s_dim    = new Color32(140, 144, 160, 255);
        static readonly Color s_accent = new Color32(100, 180, 255, 255);

        #endregion

        static TMP_FontAsset s_overrideFont;
        static GMUGUIPanel s_instance;

        public static TMP_FontAsset OverrideFont
        {
            get => s_overrideFont;
            set
            {
                s_overrideFont = value;
                if (value != null) s_instance?.ApplyFont(value);
            }
        }

        IReadOnlyList<GMCategoryInfo> _categories;
        TMP_FontAsset _font;
        int _activeTab;
        ScrollRect _scroll;
        readonly List<TabSlot> _tabs = new();
        readonly List<RectTransform> _pages = new();

        public GameObject Blocker;
        public bool IsVisible => gameObject.activeSelf;

        struct TabSlot
        {
            public Image Bg;
            public TextMeshProUGUI Label;
            public LayoutElement Layout;
        }

        #region Public API

        public static GMUGUIPanel Create(Transform parent, IReadOnlyList<GMCategoryInfo> categories)
        {
            var rt = NewRT("GMPanel", parent);
            Stretch(rt);

            var panel = rt.gameObject.AddComponent<GMUGUIPanel>();
            panel._categories = categories;
            panel.Build();
            rt.gameObject.SetActive(false);
            s_instance = panel;
            return panel;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (Blocker) Blocker.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (Blocker) Blocker.SetActive(false);
        }

        void ApplyFont(TMP_FontAsset font)
        {
            _font = font;
            var root = transform.root;
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                t.font = font;
            RefreshTabWidths();
        }

        void RefreshTabWidths()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                var label = _tabs[i].Label;
                label.ForceMeshUpdate();
                _tabs[i].Layout.preferredWidth = Mathf.Max(100, label.preferredWidth + 48);
            }
        }

        #endregion

        #region Build

        TMP_FontAsset CachedFont => _font ??= OverrideFont != null
            ? OverrideFont
            : TMP_Settings.defaultFontAsset;

        void Build()
        {
            var root = (RectTransform)transform;

            var bg = Img(root.gameObject, s_bg);
            bg.raycastTarget = true;

            const float hH = 100f, tH = 72f, sH = 2f;

            BuildHeader(root, hH);
            BuildTabBar(root, hH, tH);
            Img(TopStrip(NewRT("Sep", root), hH + tH, sH).gameObject, s_sep);
            BuildContent(root, hH + tH + sH);

            if (_categories.Count > 0)
                SelectTab(0);
        }

        void BuildHeader(RectTransform root, float h)
        {
            var hdr = TopStrip(NewRT("Header", root), 0, h);
            Img(hdr.gameObject, s_header);

            var crt = NewRT("Close", hdr);
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = new Vector2(0, 1);
            crt.pivot = new Vector2(0, 0.5f);
            crt.offsetMin = new Vector2(10, 10);
            crt.offsetMax = new Vector2(h, -10);
            Img(crt.gameObject, s_close);
            var cBtn = AddSmartButton(crt.gameObject);
            cBtn.onClick.AddListener(Hide);

            var xTxt = Txt(NewRT("X", crt), "X", 38, Color.white, FontStyles.Bold);
            Stretch(xTxt.rectTransform);
            xTxt.alignment = TextAlignmentOptions.Center;

            var trt = NewRT("Title", hdr);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(h, 0);
            trt.offsetMax = new Vector2(-h, 0);
            var title = Txt(trt, "GM Console", 42, s_text);
            title.alignment = TextAlignmentOptions.Center;
        }

        void BuildTabBar(RectTransform root, float top, float h)
        {
            var srt = TopStrip(NewRT("TabScroll", root), top, h);

            var vp = NewRT("Viewport", srt);
            Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            vp.gameObject.AddComponent<NonDrawingGraphic>().raycastTarget = true;

            var ct = NewRT("Content", vp);
            ct.anchorMin = new Vector2(0, 0);
            ct.anchorMax = new Vector2(0, 1);
            ct.pivot = new Vector2(0, 0.5f);
            ct.sizeDelta = Vector2.zero;

            var hlg = ct.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 8;
            hlg.padding = new RectOffset(20, 20, 6, 6);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            ct.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var sr = srt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.viewport = vp;
            sr.content = ct;

            for (int i = 0; i < _categories.Count; i++)
            {
                int idx = i;
                var tab = NewRT($"Tab{i}", ct);
                var le = tab.gameObject.AddComponent<LayoutElement>();

                var tabImg = Img(tab.gameObject, s_tabN);
                var tabTxt = Txt(NewRT("L", tab), _categories[i].Category, 32, s_dim);
                Stretch(tabTxt.rectTransform);
                tabTxt.alignment = TextAlignmentOptions.Center;

                le.preferredWidth = Mathf.Max(100, tabTxt.preferredWidth + 48);
                le.preferredHeight = h - 12;

                var btn = AddSmartButton(tab.gameObject, enableScale: false);
                btn.onClick.AddListener(() => SelectTab(idx));

                _tabs.Add(new TabSlot { Bg = tabImg, Label = tabTxt, Layout = le });
            }
        }

        void BuildContent(RectTransform root, float top)
        {
            var srt = NewRT("Scroll", root);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = new Vector2(0, -top);

            var vp = NewRT("Viewport", srt);
            Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            vp.gameObject.AddComponent<NonDrawingGraphic>().raycastTarget = true;

            var sr = srt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.viewport = vp;
            _scroll = sr;

            for (int i = 0; i < _categories.Count; i++)
            {
                var page = NewRT($"Page{i}", vp);
                page.anchorMin = new Vector2(0, 1);
                page.anchorMax = new Vector2(1, 1);
                page.pivot = new Vector2(0.5f, 1);
                page.sizeDelta = Vector2.zero;

                var vlg = page.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.spacing = 16;
                vlg.padding = new RectOffset(20, 20, 16, 20);
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                page.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;

                BuildPageCommands(page, _categories[i].Commands);

                page.gameObject.SetActive(false);
                _pages.Add(page);
            }
        }

        void BuildPageCommands(RectTransform page, List<GMCommandInfo> commands)
        {
            var groups = new List<(string Group, List<GMCommandInfo> Cmds)>();
            foreach (var cmd in commands)
            {
                var g = cmd.Group;
                if (groups.Count > 0 && groups[^1].Group == g)
                {
                    groups[^1].Cmds.Add(cmd);
                }
                else
                {
                    groups.Add((g, new List<GMCommandInfo> { cmd }));
                }
            }

            foreach (var (group, cmds) in groups)
            {
                if (string.IsNullOrEmpty(group))
                {
                    foreach (var cmd in cmds)
                        BuildCard(page, cmd);
                }
                else
                {
                    BuildGroup(page, group, cmds);
                }
            }
        }

        void BuildGroup(RectTransform page, string groupName, List<GMCommandInfo> commands)
        {
            var groupRoot = NewRT($"G_{groupName}", page);
            var vlg = groupRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = NewRT("Header", groupRoot);
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            Img(hdr.gameObject, s_sep);

            var hdrHlg = hdr.gameObject.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.spacing = 8;
            hdrHlg.padding = new RectOffset(16, 16, 8, 8);
            hdrHlg.childAlignment = TextAnchor.MiddleLeft;
            hdrHlg.childForceExpandWidth = false;
            hdrHlg.childForceExpandHeight = false;

            var arrowRt = NewRT("Arrow", hdr);
            arrowRt.gameObject.AddComponent<LayoutElement>().preferredWidth = 32;
            var arrow = Txt(arrowRt, "\u25BC", 24, s_dim);
            arrow.alignment = TextAlignmentOptions.Center;

            var titleRt = NewRT("Title", hdr);
            titleRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Txt(titleRt, groupName, 30, s_text, FontStyles.Bold);

            var content = NewRT("Content", groupRoot);
            var cVlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            cVlg.spacing = 16;
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;

            foreach (var cmd in commands)
                BuildCard(content, cmd);

            var btn = AddSmartButton(hdr.gameObject, enableScale: false);
            btn.onClick.AddListener(() =>
            {
                var active = !content.gameObject.activeSelf;
                content.gameObject.SetActive(active);
                arrow.text = active ? "\u25BC" : "\u25B6";
            });
        }

        void BuildCard(RectTransform parent, GMCommandInfo cmd)
        {
            var card = NewRT("Card", parent);
            Img(card.gameObject, s_card);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.padding = new RectOffset(20, 20, 16, 16);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var nrt = NewRT("Name", card);
            nrt.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            Txt(nrt, cmd.DisplayName, 34, s_accent);

            Func<object>[] bindings = Array.Empty<Func<object>>();

            if (cmd.Params.Length > 0)
            {
                var sepRt = NewRT("Sep", card);
                sepRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1;
                Img(sepRt.gameObject, s_sep);

                bindings = new Func<object>[cmd.Params.Length];
                for (int i = 0; i < cmd.Params.Length; i++)
                    bindings[i] = AddParam(card, cmd.Params[i]);
            }

            var row = NewRT("RunRow", card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            var rhlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rhlg.childAlignment = TextAnchor.MiddleRight;
            rhlg.childForceExpandWidth = false;
            rhlg.childForceExpandHeight = false;

            NewRT("Spacer", row).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var brt = NewRT("Run", row);
            var ble = brt.gameObject.AddComponent<LayoutElement>();
            ble.preferredWidth = 150;
            ble.preferredHeight = 56;
            Img(brt.gameObject, s_run);
            var bBtn = AddSmartButton(brt.gameObject);
            bBtn.onClick.AddListener(() => Exec(cmd, bindings));

            var bTxt = Txt(NewRT("L", brt), "\u6267\u884c", 32, Color.white);
            Stretch(bTxt.rectTransform);
            bTxt.alignment = TextAlignmentOptions.Center;
        }

        #endregion

        #region Param Widgets

        Func<object> AddParam(RectTransform parent, GMParamInfo p)
        {
            var row = NewRT("P", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;

            var lrt = NewRT("Label", row);
            lrt.gameObject.AddComponent<LayoutElement>().preferredWidth = 220;
            Txt(lrt, p.DisplayName, 32, s_dim);

            if (p.ParamType == typeof(bool))
                return BoolWidget(row, p.DefaultValue is true);
            if (p.ParamType.IsEnum)
                return EnumWidget(row, p);
            return InputWidget(row, p);
        }

        Func<object> InputWidget(RectTransform parent, GMParamInfo p)
        {
            var rt = NewRT("Input", parent);
            rt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Img(rt.gameObject, s_input);

            var area = NewRT("Area", rt);
            Stretch(area);
            area.offsetMin = new Vector2(12, 4);
            area.offsetMax = new Vector2(-12, -4);
            area.gameObject.AddComponent<RectMask2D>();

            var txt = Txt(area, "", 32, s_text);
            Stretch(txt.rectTransform);
            txt.richText = false;

            var ph = Txt(NewRT("PH", area), "...", 32,
                new Color(s_dim.r, s_dim.g, s_dim.b, 0.5f), FontStyles.Italic);
            Stretch(ph.rectTransform);

            var inp = rt.gameObject.AddComponent<TMP_InputField>();
            inp.textComponent = txt;
            inp.placeholder = ph;
            inp.text = p.DefaultValue?.ToString() ?? "";
            inp.fontAsset = CachedFont;

            if (p.ParamType == typeof(int))
                inp.contentType = TMP_InputField.ContentType.IntegerNumber;
            else if (p.ParamType == typeof(float))
                inp.contentType = TMP_InputField.ContentType.DecimalNumber;

            return () => ParseInput(p, inp.text);
        }

        Func<object> BoolWidget(RectTransform parent, bool init)
        {
            var state = new[] { init };

            var rt = NewRT("Bool", parent);
            rt.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
            var img = Img(rt.gameObject, init ? s_accent : s_input);

            var lbl = Txt(NewRT("L", rt), init ? "ON" : "OFF", 32,
                init ? Color.white : s_dim);
            Stretch(lbl.rectTransform);
            lbl.alignment = TextAlignmentOptions.Center;

            var btn = AddSmartButton(rt.gameObject, enableScale: false);
            btn.onClick.AddListener(() =>
            {
                state[0] = !state[0];
                img.color = state[0] ? s_accent : s_input;
                lbl.text = state[0] ? "ON" : "OFF";
                lbl.color = state[0] ? Color.white : s_dim;
            });

            return () => (object)state[0];
        }

        Func<object> EnumWidget(RectTransform parent, GMParamInfo p)
        {
            var names = Enum.GetNames(p.ParamType);
            var sel = new[] { Mathf.Max(0, Array.IndexOf(names, p.DefaultValue?.ToString())) };
            var imgs = new List<Image>();
            var txts = new List<TextMeshProUGUI>();

            var rt = NewRT("Enum", parent);
            rt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var ort = NewRT($"E{i}", rt);
                var oImg = Img(ort.gameObject, i == sel[0] ? s_tabA : s_input);
                var oTxt = Txt(NewRT("L", ort), names[i], 28,
                    i == sel[0] ? Color.white : s_dim);
                Stretch(oTxt.rectTransform);
                oTxt.alignment = TextAlignmentOptions.Center;

                imgs.Add(oImg);
                txts.Add(oTxt);

                var ob = AddSmartButton(ort.gameObject, enableScale: false);
                ob.onClick.AddListener(() =>
                {
                    sel[0] = idx;
                    for (int j = 0; j < imgs.Count; j++)
                    {
                        imgs[j].color = j == idx ? s_tabA : s_input;
                        txts[j].color = j == idx ? Color.white : s_dim;
                    }
                });
            }

            return () => Enum.Parse(p.ParamType, names[sel[0]]);
        }

        #endregion

        #region Interaction

        void SelectTab(int idx)
        {
            if (idx < 0 || idx >= _categories.Count) return;

            _activeTab = idx;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Bg.color = i == idx ? s_tabA : s_tabN;
                _tabs[i].Label.color = i == idx ? Color.white : s_dim;
            }

            for (int i = 0; i < _pages.Count; i++)
                _pages[i].gameObject.SetActive(i == idx);

            if (idx < _pages.Count)
            {
                _scroll.content = _pages[idx];
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        void Exec(GMCommandInfo cmd, Func<object>[] bindings)
        {
            try
            {
                var args = new object[bindings.Length];
                for (int i = 0; i < bindings.Length; i++)
                    args[i] = bindings[i]();
                cmd.Invoke(args);
                JLogger.Log($"[GM] Executed: {cmd.DisplayName}");
                if (cmd.CloseAfter) Hide();
            }
            catch (TargetInvocationException e)
            {
                JLogger.LogException(e.InnerException ?? e);
            }
            catch (Exception e)
            {
                JLogger.LogException(e);
            }
        }

        static object ParseInput(GMParamInfo p, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return p.DefaultValue;
            var t = p.ParamType;
            if (t == typeof(int)) return int.TryParse(raw, out var iv) ? iv : p.DefaultValue;
            if (t == typeof(float)) return float.TryParse(raw, out var fv) ? fv : p.DefaultValue;
            if (t == typeof(string)) return raw;
            return p.DefaultValue;
        }

        #endregion

        #region Helpers

        static RectTransform NewRT(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static RectTransform TopStrip(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, -top);
            return rt;
        }

        static Image Img(GameObject go, Color c)
        {
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        TextMeshProUGUI Txt(RectTransform rt, string text, float size, Color c,
            FontStyles style = FontStyles.Normal)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = CachedFont;
            t.fontSize = size;
            t.color = c;
            t.fontStyle = style;
            t.text = text;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableWordWrapping = false;
            return t;
        }

        static Button AddSmartButton(GameObject go, bool enableScale = true)
        {
            var btn = go.AddComponent<Button>();
            return btn;
        }

        #endregion
    }
}
#endif
